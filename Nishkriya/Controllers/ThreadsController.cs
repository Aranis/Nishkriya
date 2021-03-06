﻿using System;
using System.Linq;
using System.Web.Mvc;
using Nishkriya.Models;
using Nishkriya.Models.ViewModels;
using Nishkriya.Properties;

namespace Nishkriya.Controllers
{
    public class ThreadsController : Controller
    {
        NishkriyaContext db = new NishkriyaContext();

        public ActionResult Index()
        {
            return Page();
        }

        public ActionResult Page(int id=1)
        {
            var pageSize = Settings.Default.ThreadsPerPage;
            var threads = db.Threads.OrderByDescending(thread => thread.Posts.Max(post => post.PostDate));

            var totalPages = (int) Math.Ceiling(threads.Count()/(float) pageSize);


            ViewBag.Title = "All Topics";
            ViewBag.selectedSidebarEntry = "All Topics";
            ViewBag.Paginator = new PaginatorViewModel
                {
                    PageIndex = id,
                    TotalPages = totalPages == 0 ? 1 : totalPages,
                    MaximumSpread = 3,
                    Action = "Page",
                    Controller = "Threads"
                };
 
            var selectedTheads = threads.Skip((id - 1) * pageSize).Take(pageSize).OrderByDescending(thread => thread.Posts.Max(post => post.PostDate));
            
            return View("Page", selectedTheads);
        }

        public ActionResult LatestTopics()
        {
            var threads = db.Threads.OrderByDescending(thread => thread.Posts.Max(post => post.PostDate)).Take(10);
                                             
            ViewBag.Title = "Latest Topics";
            ViewBag.selectedSidebarEntry = "Latest Topics";

            return View(threads);
        }

        public ActionResult Details(int id)
        {
            var thread = db.Threads.SingleOrDefault(t => t.ThreadId == id);
            ViewBag.Title = thread.Title;

            return View(thread);
        }
    }
}
