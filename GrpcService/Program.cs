using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace GrpcService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // CreateHostBuilder(args).Build().Run();

            WebHost.CreateDefaultBuilder()
                .ConfigureKestrel(options =>
                {
                    options.Listen(IPAddress.Any, 8091, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                        listenOptions.UseHttps();
                        listenOptions.KestrelServerOptions.Limits.Http2.MaxStreamsPerConnection = 2 * Environment.ProcessorCount;
                    });
                })
                .UseStartup<Http2Startup>()
                .Build()
                .Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        private class Http2Startup
        {

            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
            {
                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }

                app.UseRouting();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/dbs/{name}", async context =>
                    {
                        context.Response.ContentType = "application/json";
                        context.Response.StatusCode = 200;

                        IHeaderDictionary responseHeaders = context.Response.Headers;
                        responseHeaders["x-ms-activity-id"] = Guid.NewGuid().ToString();
                        responseHeaders["x-ms-request-charge"] = "1.0";
                        responseHeaders["x-ms-session-token"] = "9898";

                        await context.Response.WriteAsync(GreeterService.testJsonPayload);
                    });
                });
            }
        }
    }
}
