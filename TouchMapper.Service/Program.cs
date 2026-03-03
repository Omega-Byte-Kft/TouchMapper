using TouchMapper.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "TouchMapper");
builder.Services.AddHostedService<TouchMapperWorker>();

var host = builder.Build();
host.Run();
