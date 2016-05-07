using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization; // System.Web.Extensions.dll
using Microsoft.ServiceFabric.Services.Client;

namespace Richter.Utilities {
   public sealed class ServicePartitionEndpointResolver {
      private readonly ServicePartitionResolver m_spr;
      public readonly Uri ServiceName;
      public readonly String EndpointName;
      public readonly ServicePartitionKey PartitionKey;
      private ResolvedServicePartition m_rsp = null;
      private String m_endpoint = null;
      public ServicePartitionEndpointResolver(ServicePartitionResolver resolver, Uri serviceName, String endpointName, ServicePartitionKey partitionKey) {
         m_spr = resolver;
         ServiceName = serviceName;
         EndpointName = endpointName;
         PartitionKey = partitionKey;
      }

      public async Task<TResult> ResolveAsync<TResult>(CancellationToken cancellationToken, Func<String, CancellationToken, Task<TResult>> func) {
         if (m_rsp == null) {
            // Get endpoints from naming service; https://msdn.microsoft.com/en-us/library/azure/dn707638.aspx
            m_rsp = await m_spr.ResolveAsync(ServiceName, PartitionKey, cancellationToken);
         }
         for (;;) {
            try {
               if (m_endpoint == null) m_endpoint = DeserializeEndpoints(m_rsp.GetEndpoint())[EndpointName];
               return await func(m_endpoint, cancellationToken);
            }
            catch (HttpRequestException ex) when ((ex.InnerException as WebException)?.Status == WebExceptionStatus.ConnectFailure) {
               m_rsp = await m_spr.ResolveAsync(m_rsp, cancellationToken);
               m_endpoint = null; // Retry after getting the latest endpoints from naming service
            }
         }
      }

      private static readonly JavaScriptSerializer s_javaScriptSerializer = new JavaScriptSerializer();
      private sealed class EndpointsCollection {
         public Dictionary<String, String> Endpoints = null;
      }
      private static IReadOnlyDictionary<String, String> DeserializeEndpoints(ResolvedServiceEndpoint partitionEndpoint) {
         return s_javaScriptSerializer.Deserialize<EndpointsCollection>(partitionEndpoint.Address).Endpoints;
      }
   }

   public static class ServiceFabricExtensions {
      public static EndpointResourceDescription GetEndpointResourceDescription(this ServiceContext context, String endpointName)
            => context.CodePackageActivationContext.GetEndpoint(endpointName);

      public static String CalcUriSuffix(this StatelessServiceContext context)
         => context.CalcUriSuffix(context.InstanceId);

      public static String CalcUriSuffix(this StatefulServiceContext context)
         => context.CalcUriSuffix(context.ReplicaId);

      private static String CalcUriSuffix(this ServiceContext context, Int64 instanceOrReplicaId)
         => $"{context.PartitionId}/{instanceOrReplicaId}" +
            $"/{Guid.NewGuid().ToByteArray().ToBase32String()}/";   // Uniqueness

      private static readonly JavaScriptSerializer s_javaScriptSerializer = new JavaScriptSerializer();
      private sealed class EndpointsCollection {
         public Dictionary<String, String> Endpoints = null;
      }

      public static async Task<String> ResolveEndpointAsync(this ServicePartitionResolver resolver, Uri namedService, ServicePartitionKey partitionKey, String endpointName, CancellationToken cancellationToken) {
         ResolvedServicePartition partition = await resolver.ResolveAsync(namedService, partitionKey, cancellationToken);
         return DeserializeEndpoints(partition.GetEndpoint())[endpointName];
      }


      public static async Task<IReadOnlyDictionary<String, String>> ResolveEndpointsAsync(this ServicePartitionResolver resolver, Uri namedService, ServicePartitionKey partitionKey, CancellationToken cancellationToken) {
         ResolvedServicePartition partition = await resolver.ResolveAsync(namedService, partitionKey, cancellationToken);
         return DeserializeEndpoints(partition.GetEndpoint());
      }

      private static IReadOnlyDictionary<String, String> DeserializeEndpoints(ResolvedServiceEndpoint partitionEndpoint) {
         return s_javaScriptSerializer.Deserialize<EndpointsCollection>(partitionEndpoint.Address).Endpoints;
      }
   }

   public sealed class PartitionEndpointResolverX {
      public readonly Uri ServiceName;
      public readonly String EndpointName;
      private static readonly ServicePartitionResolver s_servicePartitionResolver
         = ServicePartitionResolver.GetDefault();

      public PartitionEndpointResolverX(Uri serviceName, String endpointName) {
         ServiceName = serviceName;
         EndpointName = endpointName;
      }
      public Task<String> ResolveEndpointAsync(ServicePartitionKey partitionKey, CancellationToken cancellationToken) {
         return s_servicePartitionResolver.ResolveEndpointAsync(ServiceName, partitionKey, EndpointName, cancellationToken);
      }
   }
}