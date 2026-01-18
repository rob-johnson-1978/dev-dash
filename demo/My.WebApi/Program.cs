using My.WebApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<Ticker>();

var app = builder.Build();

app.Run();
