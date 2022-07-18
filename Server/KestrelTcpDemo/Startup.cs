using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace KestrelTcpDemo
{
    public class Startup
    {
        private static readonly byte[] testPayload = Encoding.UTF8.GetBytes(File.ReadAllText("TestData.json"));

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IComputeHash>((sp) => new StringHMACSHA256Hash(InMemoryRntbd2ConnectionHandler.AuthKey));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            //app.UseDeveloperExceptionPage();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/dbs/{dbId}/colls/{collId}/docs/{docId}", async (string dbId, string collId, string docId, HttpContext context) =>
                {
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

                        IComputeHash computeHash = context.RequestServices.GetService<IComputeHash>();
                        if (computeHash == null)
                        {
                            throw new Exception("IComputeHash is not configured");
                        }

                        string expectedAuthValue = AuthorizationHelper.GenerateKeyAuthorizationCore("GET",
                            dateHeaderValue,
                            "docs",
                            String.Format($"dbs/{dbId}/colls/{collId}/docs/{docId}"),
                            computeHash);

                        if (expectedAuthValue != authTokenValue)
                        {
                            //Console.WriteLine($"xDate: {xDateValue} expected: {expectedAuthValue} actual: {authTokenValue} ");

                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsync($"Auth validation failed ResourceId={context.Request.Path}, xDate={xDateValue}");
                            return;
                        }

                        context.Response.Headers.ContentLength = testPayload.Length;
                        context.Response.Headers.Add(HttpConstants.HttpHeaders.RequestCharge, "1.0");

                        PipeWriter pipeWriter = context.Response.BodyWriter;
                        Memory<byte> outputBuffer = pipeWriter.GetMemory(testPayload.Length);
                        testPayload.CopyTo(outputBuffer);
                        pipeWriter.Advance(testPayload.Length);
                    }
                });
            });
        }
    }
}
