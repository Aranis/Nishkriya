﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Nishkriya.Models;
using Nishkriya.Properties;

namespace Nishkriya.Scraper
{
    public class ScavengerLord
    {
        private readonly IHashProvider _hashProvider;

        public ScavengerLord(IHashProvider hashProvider)
        {
            _hashProvider = hashProvider;
        }

        public void Scrape()
        {
            using (var db = new NishkriyaContext())
            {
                var session = new ScraperSession { Start = DateTime.Now };

                db.Accounts.Where(a => a.Active).ToList().ForEach(account =>
                    {
                        var toAdd = GetNewPosts(account, db.Threads.ToList(), session).ToList();
                        session.PostsAdded += toAdd.Count;
                        account.Posts.AddRange(toAdd);
                        db.SaveChanges(); //Pesky thread duplication avoided
                    });

                session.Finish = DateTime.Now;

                db.Stats.Add(session);
                db.SaveChanges();
            }
        }

        private HtmlDocument CleanHtml(HtmlDocument document)
        {
            //Make the Spoilers have a unique id and remove the display:none
            var spoilers = document.DocumentNode.SelectNodes("//*[contains(@class, 'spoilerbox')]");            
            if(spoilers !=  null)
            {
                foreach (var node in spoilers)
                {
                    node.Id = "spoilerId";
                    node.SetAttributeValue("style", "");
                }
            }

            //Remove Spoiler buttons
            var spoilersButtons = document.DocumentNode.SelectNodes("//*[contains(@class, 'spoilertitle')]");
            if (spoilers != null)
            {
                foreach (var node in spoilersButtons)
                {
                    node.ParentNode.ChildNodes.Remove(node);
                }
            }

            //Remove signatures completly
            var signatures = document.DocumentNode.SelectNodes("//*[contains(@class, 'yafsignature')]");
            if (signatures != null)
            {
                foreach (var node in signatures)
                {
                    node.ParentNode.ChildNodes.Remove(node);
                }
            }

            return document;
        }

        private IEnumerable<Post> GetNewPosts(ForumAccount account, List<Thread> threads, ScraperSession session)
        {
            try
            {                                
                var url = string.Format(Settings.Default.ProfileUrl, account.ForumId);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2";
                req.CookieContainer = new CookieContainer();
                req.CookieContainer.Add(new Cookie(".YAFNET_Authentication", Settings.Default.AuthToken, "/", "forums.white-wolf.com"));

                var responseStream = req.GetResponse().GetResponseStream();
                var document = new HtmlDocument();

                if (responseStream == null)
                {
                    throw new NoNullAllowedException();
                }

                using (var reader = new StreamReader(responseStream))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var writer = new StreamWriter(memoryStream))
                        {
                            writer.Write(reader.ReadToEnd());
                            memoryStream.Position = 0;
                            document.Load(memoryStream, new UTF8Encoding());
                        }
                    }
                }

                document = CleanHtml(document);

                var postsCollection = new List<Post>();

                const string placeholderFragment = "id('MasterPageContentPlaceHolder_forum_ctl01_ProfileTabs_Last10PostsTab')//table//tr[";
                const string anchorSelectorFragment = "]//td/a/@href";
                const string titleSelectorFragment = "]//td/a/text()";
                const string dateSelectorFragment = "]//td/text()[4]";

                foreach (int i in Enumerable.Range(0, 10))
                {
                    var tableRow = 1 + (2 * i);

                    var threadIdSelector = String.Format("{0}{1}{2}", placeholderFragment, tableRow, anchorSelectorFragment);
                    var titleSelector = String.Format("{0}{1}{2}", placeholderFragment, tableRow, titleSelectorFragment);
                    var dateSelector = String.Format("{0}{1}{2}", placeholderFragment, tableRow, dateSelectorFragment);

                    var threadHref = document.DocumentNode.SelectSingleNode(threadIdSelector).Attributes[0].Value;
                    var threadId = int.Parse(Regex.Match(threadHref, @"(\d+)$").Groups[0].Value);

                    var threadTitle = document.DocumentNode.SelectSingleNode(titleSelector).InnerHtml.Trim();

                    var postDate = DateTime.Parse(document.DocumentNode.SelectSingleNode(dateSelector).InnerHtml.Trim());

                    var postContent =
                        document.DocumentNode.SelectSingleNode(
                            "id('MasterPageContentPlaceHolder_forum_ctl01_ProfileTabs_Last10PostsTab_LastPosts_MessagePost_" + i + "')").InnerHtml;

                    Thread thread = threads.SingleOrDefault(s => s.ThreadId == threadId);
                    if (thread == null)
                    {
                        thread = new Thread { ThreadId = threadId, Title = threadTitle };
                        threads.Add(thread);
                        session.ThreadsAdded++;
                    }

                    postsCollection.Add(new Post
                    {
                        Content = postContent,
                        Hash = _hashProvider.Compute(postContent),
                        PostDate = postDate,
                        Thread = thread
                    });
                }

                return postsCollection.Where(newPost => !account.Posts.Select(p => p.Hash).Contains(newPost.Hash));
            }
            catch (Exception ex)
            {
                session.HadErrors = true;
                session.Errors.Add(new Error
                    {
                        Message = ex.Message,
                        Source = ex.Source,
                        StackTrace = ex.StackTrace,
                        TargetSite = ex.TargetSite.Name
                    });

                Debug.WriteLine(ex.Message + "\n----\n" + ex.StackTrace);
                return new Post[0];
            }
        }
    }
}
