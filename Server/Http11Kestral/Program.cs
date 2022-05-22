using Http11Kestral;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;

//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Http11Kestral
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<AuthorizationTokenProvider>((sp) => AuthorizationTokenProviderMasterKey.NewEmulatorAuthProvider());
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/dbs/{dbId}/colls/{collId}/docs/{docId}", async context =>
                {
                    string? dbId = (string?)context.GetRouteValue("dbId");
                    string? collId = (string?)context.GetRouteValue("collId");
                    string? docId = (string?)context.GetRouteValue("docId");
                    if (dbId == null || collId == null || docId == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync($"{DateTime.UtcNow.ToString()} : Incorrect addressing ({dbId}, {collId}, {docId}) ");
                        return;
                    }

                    IHeaderDictionary inputHeaders = context.Request.Headers;
                    if (inputHeaders != null)
                    {
                        StringValues authHeaderValue;
                        StringValues dateHeaderValue;

                        inputHeaders.TryGetValue("authorization", out authHeaderValue);
                        inputHeaders.TryGetValue("x-ms-date", out dateHeaderValue);

                        if (authHeaderValue.Count != 1
                            || dateHeaderValue.Count != 1)
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsync($"{DateTime.UtcNow.ToString()} , Missing headers (auth={authHeaderValue.FirstOrDefault()}, date={dateHeaderValue.FirstOrDefault()})");
                            return;
                        }

                        string authTokenValue = authHeaderValue.First().Trim();
                        string xDateValue = dateHeaderValue.First().Trim();

                        if (string.IsNullOrWhiteSpace(authTokenValue) || string.IsNullOrWhiteSpace(xDateValue))
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsync($"{DateTime.UtcNow.ToString()} , Missing headers (auth={authHeaderValue.FirstOrDefault()}, date={dateHeaderValue.FirstOrDefault()})");
                            return;
                        }

                        AuthorizationTokenProvider? authorizationTokenProvider = context.RequestServices.GetService<AuthorizationTokenProvider>();
                        if (authorizationTokenProvider == null)
                        {
                            throw new Exception("AuthorizationTokenProvider is not configured");
                        }

                        string expectedAuthValue = authorizationTokenProvider.DocumentReadAuthorizationToken(context.Request.Path, xDateValue);
                        if (expectedAuthValue != authTokenValue)
                        {
                            Console.WriteLine($"xDate: {xDateValue} expected: {expectedAuthValue} actual: {authTokenValue} ");

                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsync($"Auth validation failed ResourceId={context.Request.Path}, xDate={xDateValue}");
                            return;
                        }
                    }

                    await context.Response.WriteAsync($"{DateTime.UtcNow.ToString()} GOOD TO GO!!");
                });
            });
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            //using (AuthorizationTokenProvider tokenProvider = AuthorizationTokenProviderMasterKey.NewEmulatorAuthProvider())
            //{
            //    string xDate = AuthorizationHelper.XDateHeader();
            //    Console.WriteLine($"XDate: {xDate} ");
            //    Console.WriteLine($"AuthHeader: {tokenProvider.DocumentReadAuthorizationToken("/dbs/db1/colls/c1/docs/d1", xDate)}");

            //    Console.ReadLine();
            //}

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
               .ConfigureWebHostDefaults(webBuilder =>
                    webBuilder.UseStartup<Startup>()
                    .UseKestrel()
                );
        }
    }
}