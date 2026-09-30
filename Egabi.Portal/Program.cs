using Egabi.Portal.Services.Applicant;
using Egabi.Portal.Services.InspectPro;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

// ═══════════════════════════════════════════════════════
// egabi Platform (InspectPro) Public API — server-to-server
// Settings: appsettings → "InspectProApi" (BaseUrl, PortalClientKey)
// ═══════════════════════════════════════════════════════
builder.Services.Configure<InspectProApiOptions>(builder.Configuration.GetSection("InspectProApi"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();   // caches the Form Builder renderer script
builder.Services.AddHttpClient<InspectProApiClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<InspectProApiOptions>>().Value;
    http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});

// ═══════════════════════════════════════════════════════
// Applicant sign-in: encrypted HttpOnly cookie holding the API token.
// Separate from Umbraco back-office users and members.
// ═══════════════════════════════════════════════════════
builder.Services.AddAuthentication()
    .AddCookie(ApplicantAuth.Scheme, options =>
    {
        options.Cookie.Name = ".EgabiPortal.Applicant";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = false;   // the cookie ends when the API token ends
    });

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseHttpsRedirection();

// Reads the applicant cookie once per request (see ApplicantAuth.Current).
app.Use(async (context, next) =>
{
    await ApplicantAuth.LoadAsync(context);
    await next();
});

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        // Portal endpoints (Controllers/PortalApiController.cs)
        u.EndpointRouteBuilder.MapControllers();

        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
