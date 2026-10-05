using System.Text.Json;
using Scalar.AspNetCore;
using DefaultNamespace;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ActorFilter>();
        options.Filters.Add<ValidationFilter>();
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        JsonSettings.Apply(options.JsonSerializerOptions);
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = InvalidModelStateResponse.Create);
builder.Services.ConfigureHttpJsonOptions(options => JsonSettings.Apply(options.SerializerOptions));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
    member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);

builder.Services.AddOpenApi(options => options.AddSchemaTransformer<EnumSchemaTransformer>());

builder.Services.Configure<AttachmentStorageOptions>(
    builder.Configuration.GetSection(AttachmentStorageOptions.Section));
builder.Services.AddSingleton<AttachmentStorage>();
builder.Services.AddHostedService<PendingObjectDrainer>();

builder.Services.AddScoped<Actor>();
builder.Services.AddScoped<Placements>();
builder.Services.AddScoped<ArchiveGuard>();
builder.Services.AddScoped<LabelMatching>();
builder.Services.AddScoped<CardCompletion>();
builder.Services.AddScoped<AttachmentCounts>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddScoped<ICheckItemService, CheckItemService>();
builder.Services.AddScoped<IChecklistService, ChecklistService>();
builder.Services.AddScoped<ILabelService, LabelService>();
builder.Services.AddScoped<IListService, ListService>();
builder.Services.AddScoped<IWorkSpaceService, WorkSpaceService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = FieldLimits.AttachmentBytes + (1024 * 1024));

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
