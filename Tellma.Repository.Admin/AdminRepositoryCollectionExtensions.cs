﻿using Microsoft.Extensions.Configuration;
using System;
using Tellma.Repository.Admin;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class AdminRepositoryCollectionExtensions
    {
        /// <summary>
        /// Registers the <see cref="AdminRepository"/> providing access the admin database.
        /// </summary>
        public static IServiceCollection AddAdminRepository(this IServiceCollection services, string connString)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (string.IsNullOrWhiteSpace(connString))
            {
                throw new ArgumentException($"'{nameof(connString)}' cannot be null or whitespace.", nameof(connString));
            }

            // Allows the Admin repository can resolve this options class and retrieve the connection string
            services.Configure<AdminRepositoryOptions>(opt =>
            {
                opt.ConnectionString = connString;
            });

            // Add services
            return services.AddSingleton<AdminRepository>();
        }
    }
}
