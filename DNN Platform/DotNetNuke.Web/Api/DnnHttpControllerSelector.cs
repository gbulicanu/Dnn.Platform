﻿// 
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// 
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dispatcher;
using System.Web.Http.Routing;
using DotNetNuke.Common;
using DotNetNuke.Services.Localization;

namespace DotNetNuke.Web.Api
{
    internal class DnnHttpControllerSelector : IHttpControllerSelector
    {
        private const string ControllerSuffix = "Controller";
        private const string ControllerKey = "controller";

        private readonly HttpConfiguration _configuration;
        private readonly Lazy<ConcurrentDictionary<string, HttpControllerDescriptor>> _descriptorCache;

        public DnnHttpControllerSelector(HttpConfiguration configuration)
        {
            Requires.NotNull("configuration", configuration);

            _configuration = configuration;
            _descriptorCache = new Lazy<ConcurrentDictionary<string, HttpControllerDescriptor>>(InitTypeCache,
                                                                                                isThreadSafe: true);
        }

        private ConcurrentDictionary<string, HttpControllerDescriptor> DescriptorCache
        {
            get { return _descriptorCache.Value; }
        }

        public HttpControllerDescriptor SelectController(HttpRequestMessage request)
        {
            Requires.NotNull("request", request);

            string controllerName = GetControllerName(request);
            IEnumerable<string> namespaces = GetNameSpaces(request);
            if (namespaces == null || !namespaces.Any() || String.IsNullOrEmpty(controllerName))
            {
                throw new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.NotFound,
                                                                            "Unable to locate a controller for " +
                                                                            request.RequestUri));
            }

            var matches = new List<HttpControllerDescriptor>();
            foreach (string ns in namespaces)
            {
                string fullName = GetFullName(controllerName, ns);

                HttpControllerDescriptor descriptor;
                if (DescriptorCache.TryGetValue(fullName, out descriptor))
                {
                    matches.Add(descriptor);
                }
            }

            if(matches.Count == 1)
            {
                return matches.First();
            }

            //only errors thrown beyond this point
            if (matches.Count == 0)
            {
                throw new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.NotFound, string.Format(Localization.GetString("ControllerNotFound", Localization.ExceptionsResourceFile), request.RequestUri, string.Join(", ", namespaces))));
            }

            throw new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.Conflict, string.Format(Localization.GetString("AmbiguousController", Localization.ExceptionsResourceFile), controllerName, string.Join(", ", namespaces))));
        }

        public IDictionary<string, HttpControllerDescriptor> GetControllerMapping()
        {
            return DescriptorCache;
        }

        private string GetFullName(string controllerName, string ns)
        {
            return string.Format("{0}.{1}{2}", ns, controllerName, ControllerSuffix).ToLowerInvariant();
        }

        private string[] GetNameSpaces(HttpRequestMessage request)
        {
            IHttpRouteData routeData = request.GetRouteData();
            if (routeData == null)
            {
                return null;
            }

            return routeData.Route.GetNameSpaces();
        }

        private string GetControllerName(HttpRequestMessage request)
        {
            IHttpRouteData routeData = request.GetRouteData();
            if (routeData == null)
            {
                return null;
            }

            // Look up controller in route data
            object controllerName;
            routeData.Values.TryGetValue(ControllerKey, out controllerName);
            return controllerName as string;
        }

        private ConcurrentDictionary<string, HttpControllerDescriptor> InitTypeCache()
        {
            IAssembliesResolver assembliesResolver = _configuration.Services.GetAssembliesResolver();
            IHttpControllerTypeResolver controllersResolver = _configuration.Services.GetHttpControllerTypeResolver();

            ICollection<Type> controllerTypes = controllersResolver.GetControllerTypes(assembliesResolver);

            var dict = new ConcurrentDictionary<string, HttpControllerDescriptor>();

            foreach (Type type in controllerTypes)
            {
                if (type.FullName != null)
                {
                    string controllerName = type.Name.Substring(0, type.Name.Length - ControllerSuffix.Length);
                    dict.TryAdd(type.FullName.ToLowerInvariant(), new HttpControllerDescriptor(_configuration, controllerName, type));
                }
            }

            return dict;
        }
    }
}
