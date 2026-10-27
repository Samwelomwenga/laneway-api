using System.Globalization;
using System.Text.Json;
using Scalar.AspNetCore;
using Laneway.Api;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture, standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateBootstrapLogger();
SelfLog.Enable(Console.Error);
AppDomain.CurrentDomain.UnhandledException += (_, unhandled) =>
{
    Log.Fatal(unhandled.ExceptionObject as Exception, "The API stopped on an unhandled exception");
    Log.CloseAndFlush();
};

var builder = WebApplication.CreateBuilder(args);

if (args is [BrevoWebhookTokens.MintCommand])
{
    Console.WriteLine(BrevoWebhookTokens.Mint(builder.Configuration));
    return;
}

builder.AddTelemetry();

builder.Services.AddControllers(options =>
    {
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

builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer<EnumSchemaTransformer>();
    options.AddDocumentTransformer<BearerSchemeTransformer>();
    options.AddOperationTransformer<BearerSchemeTransformer>();
});

builder.Services.Configure<AttachmentStorageOptions>(
    builder.Configuration.GetSection(AttachmentStorageOptions.Section));
builder.Services.AddSingleton<AttachmentStorage>();
builder.Services.AddHostedService<PendingObjectDrainer>();
builder.Services.AddSingleton<CopyJobSignal>();
builder.Services.AddSingleton<CopyJobClaims>();
builder.Services.AddHostedService<CopyJobWorker>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddBearerTokens();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<SessionSweeper>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SessionSweeper>());
builder.Services.AddSingleton<AuthSweeper>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<AuthSweeper>());
builder.Services.AddSingleton<InviteSweeper>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<InviteSweeper>());
builder.Services.AddSingleton<Passwords>();
builder.Services.AddHttpClient<BreachedPasswords>(BreachedPasswords.Configure).RemoveAllLoggers();
builder.Services.AddScoped<PasswordPolicy>();
builder.Services.AddMessages();

builder.Services.AddScoped<Actor>();
builder.Services.AddScoped<Readable>();
builder.Services.AddSingleton<DenialMeter>();
builder.Services.AddSingleton<DenialLog>();
builder.Services.AddHostedService<DenialDrainer>();
builder.Services.AddScoped<ActivityWriter>();
builder.Services.AddScoped<ActivityTree>();
builder.Services.AddScoped<Placements>();
builder.Services.AddScoped<ArchiveGuard>();
builder.Services.AddScoped<LabelMatching>();
builder.Services.AddScoped<BoardCaps>();
builder.Services.AddScoped<CardCompletion>();
builder.Services.AddScoped<CardReads>();
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
builder.Services.AddScoped<IBoardMembersService, BoardMembersService>();
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
builder.Services.AddScoped<IWorkspaceMembersService, WorkspaceMembersService>();
builder.Services.AddScoped<Invites>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHttpHardening(builder.Configuration);
builder.Services.AddBrowserCors();
builder.Services.AddRateLimits();

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = FieldLimits.AttachmentBytes + (1024 * 1024);
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"ConnectionStrings:DefaultConnection is missing. Run the app through `doppler run --` or set the CONNECTIONSTRINGS__DEFAULTCONNECTION environment variable.");
}

if (new NpgsqlConnectionStringBuilder(connectionString).IncludeErrorDetail)
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection sets Include Error Detail, which puts row values into Postgres errors and from there into logs and spans. Remove it.");
}

builder.Services.AddDbContext<ApplicationDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(services.GetRequiredService<DeliveryCommitPoke>()));


var app = builder.Build();
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(app.Configuration[TelemetryPipeline.OtlpEndpoint]))
{
    Log.Warning("{Variable} isn't set, so no logs, traces, or metrics ship", TelemetryPipeline.OtlpEndpoint);
}

await Log.CloseAndFlushAsync();
app.Services.GetRequiredService<AuthMeter>();
app.Services.GetRequiredService<Passwords>();

app.UseTraceIdentifier();
app.UseTrustedForwardedHeaders();
app.UseRequestLine();
app.UseSecurityHeaders(app.Environment);
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient))
        .AllowAnonymous();
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).AllowAnonymous().ExcludeFromDescription();
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.RefusePlainHttp(app.Environment);
app.RefuseFailedBearerTokens();
app.UseAuthorization();

app.MapControllers();

app.Run();
