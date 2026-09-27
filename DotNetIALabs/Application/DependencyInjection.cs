using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationInfrastructure(
       this IServiceCollection services,
       IConfiguration configuration)
        {
            services.AddSingleton<IQSLAbsIA, QSLabsIA>();
            return services;
        }
    }
}
