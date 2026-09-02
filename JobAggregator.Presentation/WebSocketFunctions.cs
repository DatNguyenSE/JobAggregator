using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using JobAggregator.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;
using JobAggregator.DataAccess;
using System;
using System.Threading.Tasks;

namespace JobAggregator.Presentation
{
    public class WebSocketFunctions
    {
        private readonly IServiceProvider _serviceProvider;

        public WebSocketFunctions()
        {
            var services = new ServiceCollection();
            
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            
            services.AddDataAccess(configuration);
            
            _serviceProvider = services.BuildServiceProvider();
        }

        public async Task<APIGatewayProxyResponse> ConnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string connectionId = request.RequestContext.ConnectionId;
            context.Logger.LogLine($"[WebSocket] Client Connected: {connectionId}");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                
                await repository.AddConnectionAsync(connectionId);

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Connected"
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"[WebSocket] Connect Error: {ex.Message}");
                return new APIGatewayProxyResponse { StatusCode = 500, Body = ex.Message };
            }
        }

        public async Task<APIGatewayProxyResponse> DisconnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string connectionId = request.RequestContext.ConnectionId;
            context.Logger.LogLine($"[WebSocket] Client Disconnected: {connectionId}");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                
                await repository.RemoveConnectionAsync(connectionId);

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Disconnected"
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"[WebSocket] Disconnect Error: {ex.Message}");
                return new APIGatewayProxyResponse { StatusCode = 500, Body = ex.Message };
            }
        }
    }
}
