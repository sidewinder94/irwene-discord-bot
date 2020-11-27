using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace irwene_discord_bot_aspcore.Initializers
{
    public class CloudRoleNameTelemetryInitializer : ITelemetryInitializer
    {
        private readonly IConfiguration _configuration;

        public CloudRoleNameTelemetryInitializer(IConfiguration configuration)
        {
            this._configuration = configuration;
        }

        public void Initialize(ITelemetry telemetry)
        {
            telemetry.Context.Cloud.RoleName = this._configuration["ApplicationInsights:RoleName"];
        }
    }
}
