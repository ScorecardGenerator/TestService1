﻿using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Cik.Magazine.ApiGateway.GraphQL;
using Cik.Magazine.ApiGateway.Middlewares;
using GraphQL;
using GraphQL.Http;
using GraphQLLib = GraphQL.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Swagger;

namespace Cik.Magazine.ApiGateway
{
    public class Startup
    {
        private IActorRef _categoryCommanderActor;
        private IActorRef _categoryQueryActor;
        private ActorSystem _systemActor;

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services
                .AddMvc()
                .AddWebApiConventions()
                .AddNewtonsoftJson(
                    opts =>
                    {
                        opts.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                        opts.SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                        opts.SerializerSettings.Formatting = Formatting.Indented;
                        opts.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    });

            // services.AddAuthorization();

            // Add swagger
            services.AddSwaggerGen(
                c =>
                {
                    c.SwaggerDoc("v1", new Info
                    {
                        Version = "v1",
                        Title = "Magazine Website API"
                    });
                    /* c.AddSecurityDefinition("oauth2", new OAuth2Scheme
                    {
                        Type = "oauth2",
                        Flow = "implicit",
                        TokenUrl = "http://localhost:9999/connect/token",
                        AuthorizationUrl = "http://localhost:9999/connect/authorize",
                        Scopes = new Dictionary<string, string>{ { "magazine-api", "Magazine API Resource" } }
                    });
                    c.OperationFilter<SecurityRequirementsOperationFilter>(); */
                    c.DescribeAllEnumsAsStrings();

                    // Set the comments path for the swagger json and ui.
                    var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                    var xmlPath = Path.Combine(basePath, "MagazineApi.xml");
                    c.IncludeXmlComments(xmlPath);
                });

            // build up the actors
            _systemActor = ActorSystem.Create("magazine-system");
            _categoryQueryActor = _systemActor.ActorOf(
                Props.Empty.WithRouter(FromConfig.Instance), "category-query-group");
            _categoryCommanderActor = _systemActor.ActorOf(
                Props.Empty.WithRouter(FromConfig.Instance), "category-commander-group");
            services.AddSingleton<IActorRefFactory>(serviceProvider => _systemActor);

            // GraphQL
            services.AddSingleton<IDocumentExecuter, DocumentExecuter>();
            services.AddSingleton<IDocumentWriter, DocumentWriter>();
            services.AddScoped<MagazineQuery>();
            // services.AddScoped<CategoryType>();
            services.AddTransient<GraphQLLib.ISchema>(sp => new GraphQLLib.Schema { Query = sp.GetService<MagazineQuery>() });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IHostingEnvironment env,
            Microsoft.Extensions.Logging.ILoggingBuilder loggerBuilder)
        {
            applicationLifetime.ApplicationStopping.Register(OnShutdown);

            loggerBuilder.AddConsole();
            loggerBuilder.AddDebug();

            /* JwtSecurityTokenHandler.DefaultInboundClaimFilter.Clear();
            app.UseIdentityServerAuthentication(new IdentityServerAuthenticationOptions 
            {
                Authority = "http://localhost:9999/",
                RequireHttpsMetadata = false // don't use this for production env
            }); */

            // TODO: need to check some of URI alive, for example check AuthHost, CategoryService...
            app.UseServiceStatus(() => Task.FromResult(true));

            app.UseMvc(routes =>
            {
                routes.MapWebApiRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            app.UseSwagger();
            app.UseSwaggerUi(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Magazine Website API V1");
                // c.ConfigureOAuth2("swagger", "secret".Sha256(), "swagger", "swagger");
            });
        }

        /// <summary>
        ///     https://stackoverflow.com/questions/35257287/kestrel-shutdown-function-in-startup-cs-in-asp-net-core
        ///     https://shazwazza.com/post/aspnet-core-application-shutdown-events/
        /// </summary>
        private void OnShutdown()
        {
            _categoryQueryActor.Tell(PoisonPill.Instance);
            _categoryCommanderActor.Tell(PoisonPill.Instance);
            _systemActor.Terminate();
        }
    }
}