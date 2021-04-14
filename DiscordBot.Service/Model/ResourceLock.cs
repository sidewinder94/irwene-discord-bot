using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Service.Model
{
    public static class ResourceLock
    {
        private static readonly ConcurrentDictionary<string, BlobLease> ResourceLeases = new();


        public static async Task<bool> AcquireLock(string ressourceId)
        {
            try
            {
                var blobServiceClient = DiscordService.ServiceProvider.GetService<BlobServiceClient>();

                var blobClient = blobServiceClient
                    .GetBlobContainerClient(DiscordService.Config["Storage:BlobContainerName"])
                    .GetBlobClient(ressourceId);

                if (!await blobClient.ExistsAsync())
                {
                    using (var ms = new MemoryStream())
                    {
                        await blobClient.UploadAsync(ms);
                    }
                }

                string existingLeaseId = null;

                if (ResourceLeases.TryGetValue(ressourceId, out var existingLease))
                {
                    existingLeaseId = existingLease.LeaseId;
                }

                var leaseClient = blobClient.GetBlobLeaseClient(existingLeaseId);
                var lease = await leaseClient.AcquireAsync(TimeSpan.FromMinutes(1));
                ResourceLeases.TryAdd(ressourceId, lease);
            }
            catch (Exception e)
            {
                ResourceLeases.TryRemove(ressourceId, out _);
                DiscordService.ServiceProvider.GetService<TelemetryClient>().TrackException(e);
                return false;
            }
            

            return true;
        }

        public static async Task ReleaseLock(string ressourceId)
        {
            try
            {
                var blobServiceClient = DiscordService.ServiceProvider.GetService<BlobServiceClient>();

                var blobClient = blobServiceClient
                    .GetBlobContainerClient(DiscordService.Config["Storage:BlobContainerName"])
                    .GetBlobClient(ressourceId);

                if (!await blobClient.ExistsAsync())
                {
                    throw new ArgumentException("Cannot trying to release never locked object");
                }

                var lease = ResourceLeases[ressourceId];

                var leaseClient = blobClient.GetBlobLeaseClient(lease.LeaseId);

                await leaseClient.ReleaseAsync();

                ResourceLeases.TryRemove(ressourceId, out _);
            }
            catch (Exception e)
            {
                DiscordService.ServiceProvider.GetService<TelemetryClient>().TrackException(e);
            }
        }

    }
}
