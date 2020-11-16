namespace App

open System
open System.Collections.Generic
open System.Linq
open System.Text.Json.Serialization
open System.Threading.Tasks
open CQRSLite.Core
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
    member _.Configuration = configuration

    // This method gets called by the runtime. Use this method to add services to the container.
    member _.ConfigureServices(services: IServiceCollection) =
        // Add framework services.
        services.AddControllers()
                .AddJsonOptions(fun options -> options.JsonSerializerOptions.Converters.Add(JsonFSharpConverter()))
                |> ignore
        services
                .AddSingleton<InventoryItemDetailView>()
                .AddSingleton<InventoryListView>()
                .AddSingleton<IQueryHandler<GetInventoryItemDetails,InventoryItemDetailsDto>>(fun di->di.GetRequiredService<InventoryItemDetailView>() :> IQueryHandler<GetInventoryItemDetails,InventoryItemDetailsDto>)
                .AddSingleton<IQueryHandler<GetInventoryItems,InventoryItemListDto list>>(fun di->di.GetRequiredService<InventoryListView>() :> IQueryHandler<GetInventoryItems,InventoryItemListDto list>)
                .AddSingleton<InMemoryDatabase>(InMemoryDatabase.Default())
                .AddSingleton<ICommandHandler, InventoryCommandHandlers>()
                .AddSingleton(dateTimeOffsetNow)
                .AddSingleton<ISession, Fakes.Session<EventsT>>()
                .AddSingleton<IEventStore<EventsT>, Fakes.EventStore<EventsT>>()
                .AddSingleton<IEventPublisher<EventsT>, Fakes.EventPublisher<EventsT>>()
                .AddSingleton<IEventListener<EventsT>>(fun di->di.GetRequiredService<InventoryItemDetailView>() :> IEventListener<EventsT>)
                .AddSingleton<IEventListener<EventsT>>(fun di->di.GetRequiredService<InventoryListView>() :> IEventListener<EventsT>)
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
