using System.Text.Json;
using Scalar.AspNetCore;
using Laneway.Api;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var gateTest = 0;

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
builder.Services.AddSingleton<CopyJobSignal>();
builder.Services.AddSingleton<CopyJobClaims>();
builder.Services.AddHostedService<CopyJobWorker>();

builder.Services.AddScoped<Actor>();
builder.Services.AddScoped<ActivityWriter>();
builder.Services.AddScoped<ActivityTree>();
builder.Services.AddScoped<Placements>();
builder.Services.AddScoped<ArchiveGuard>();
builder.Services.AddScoped<LabelMatching>();
builder.Services.AddScoped<CardCompletion>();
builder.Services.AddScoped<CardSnapshots>();
builder.Services.AddScoped<ListSnapshots>();
builder.Services.AddScoped<BoardSnapshots>();
builder.Services.AddScoped<CopyJobSweep>();
builder.Services.AddScoped<ObjectCopies>();
builder.Services.AddScoped<AttachmentCounts>();
builder.Services.AddScoped<CommentCounts>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<IBoardCopyService, BoardCopyService>();
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddScoped<ICardCopyService, CardCopyService>();
builder.Services.AddScoped<ICheckItemService, CheckItemService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IChecklistService, ChecklistService>();
builder.Services.AddScoped<ILabelService, LabelService>();
builder.Services.AddScoped<IListService, ListService>();
builder.Services.AddScoped<IListCopyService, ListCopyService>();
builder.Services.AddScoped<ICopyJobService, CopyJobService>();
builder.Services.AddScoped<ICopyJobRunner, ListCopyRunner>();
builder.Services.AddScoped<ICopyJobRunner, BoardCopyRunner>();
builder.Services.AddScoped<IWorkSpaceService, WorkSpaceService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = FieldLimits.AttachmentBytes + (1024 * 1024));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"ConnectionStrings:DefaultConnection is missing. Run the app through `doppler run --` or set the CONNECTIONSTRINGS__DEFAULTCONNECTION environment variable.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));


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
