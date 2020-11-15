namespace App

open System
open System.Collections.Generic
open System.Linq
open System.Threading.Tasks
open CQRSLite.Core.Domain.ReadModel
open CQRSLite.Core.Domain.WriteModel
open CQRSLite.Core.Infrastructure
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.HttpsPolicy;
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

type Startup(configuration: IConfiguration) =
    let dateTimeOffsetNow= fun ()->DateTimeOffset.UtcNow
    let registerInventoryCommandHandlers (di:IServiceProvider)=
      InventoryCommandHandlers(di.GetRequiredService<_>(),dateTimeOffsetNow)
      :>ICommandHandler
    member _.Configuration = configuration

    // This method gets called by the runtime. Use this method to add services to the container.
    member _.ConfigureServices(services: IServiceCollection) =
        // Add framework services.
        services.AddControllers() |> ignore
        services.AddSingleton<IQueryHandler<GetInventoryItemDetails,InventoryItemDetailsDto>,InventoryItemDetailView>() 
                .AddSingleton<IQueryHandler<GetInventoryItems,InventoryItemListDto list>,InventoryListView>()
                .AddSingleton<InMemoryDatabase>()
                .AddSingleton<ICommandHandler>(registerInventoryCommandHandlers)
                .AddSingleton<ISession,FakeSession>()
                |> SwaggerConfig.configureServices
                |> ignore
    
    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    member _.Configure(app: IApplicationBuilder, env: IWebHostEnvironment) =
        if (env.IsDevelopment()) then
            app.UseDeveloperExceptionPage() |> ignore
        app.UseHttpsRedirection()
           .UseRouting()
           .UseAuthorization()
           .UseEndpoints(fun endpoints ->
                endpoints.MapControllers() |> ignore
            ) |> ignore
        SwaggerConfig.configure app |> ignore
