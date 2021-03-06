using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Azure;
using DiscordBot.Service;
using irwene_discord_bot_aspcore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights.SnapshotCollector;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using irwene_discord_bot_aspcore.Initializers;
using Microsoft.ApplicationInsights.Extensibility;

namespace irwene_discord_bot_aspcore
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ITelemetryInitializer, CloudRoleNameTelemetryInitializer>();

            services.AddApplicationInsightsTelemetry(instrumentationKey: Configuration["appinsight-instr-key"]);

            services.ConfigureTelemetryModule<QuickPulseTelemetryModule>((module, o) => module.AuthenticationApiKey = Configuration["appinsights-authentication-apikey"]);

            services.AddControllersWithViews();

            services.AddAzureClients(builder =>
            {
                builder.AddBlobServiceClient(connectionString: Configuration["secret-azure-blobs"]);
            });

            services.AddSingleton<DiscordService>();

            services.AddSingleton<BackgroundDiscordService>();
            services.AddHostedService(provider => provider.GetService<BackgroundDiscordService>());

            services.AddSnapshotCollector((configuration) => Configuration.Bind(nameof(SnapshotCollectorConfiguration), configuration));         
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime applicationLifetime, ILoggerFactory loggerFactory)
        {
            var service = app.ApplicationServices.GetService<DiscordService>();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                //service.Start();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                
                if (Configuration["HSTS_STATUS"] != "Disabled")
                {
                    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                    app.UseHsts();
                    app.UseHttpsRedirection();
                }
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });

            applicationLifetime.ApplicationStopping.Register(() => this.Shutdown(service));
        }

        private void Shutdown(DiscordService service)
        {
            service.Stop();
        }
    }
}
