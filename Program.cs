using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using DefaultNamespace;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddScoped<ILabelService, LabelService>();
builder.Services.AddScoped<IListService, ListService>();
builder.Services.AddScoped<IWorkSpaceService, WorkSpaceService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));
    

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Redirect("/scalar/v1"));

app.MapControllers();

app.Run();
