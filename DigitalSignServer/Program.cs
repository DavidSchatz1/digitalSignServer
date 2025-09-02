
using DigitalSignServer.context;
using DigitalSignServer.models;
using DigitalSignServer.Options;
using DigitalSignServer.Reposetories;
using DigitalSignServer.services;
using DigitalSignServer.Services;
using DigitalSignServer.Storage;
using Amazon.S3;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Syncfusion.Licensing;

namespace DigitalSignServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var syncfusionLicense = builder.Configuration["Syncfusion:LicenseKey"];
            if (string.IsNullOrWhiteSpace(syncfusionLicense))
            {
                // אל תדפיס את המפתח ללוגים
                throw new InvalidOperationException("Syncfusion license key is missing. Set it via dotnet user-secrets.");
            }

            SyncfusionLicenseProvider.RegisterLicense(syncfusionLicense);

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddLogging();


            // Add services to the container.
            var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                                  policy =>
                                  {
                                      policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
                                            .AllowAnyHeader()
                                            .AllowAnyMethod()
                                            .AllowCredentials();
                                  });
            });
            // קונפיגורציית Options – קוראת גם מ-appsettings וגם מ-UserSecrets (Development)
            builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
            builder.Services.Configure<PublicOptions>(builder.Configuration.GetSection("Public"));

            builder.Services.AddScoped<INotificationService, SmtpEmailService>();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
            builder.Services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
            builder.Services.AddScoped<ILawyerRepository, LawyerRepository>();
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
            builder.Services.AddScoped<ICustomerAuthRepository, CustomerAuthRepository>();
            builder.Services.AddScoped<ICustomerAuthService, CustomerAuthService>();
            builder.Services.AddScoped<TemplateFillService>();
            builder.Services.AddScoped<ITemplateFillRepository, TemplateFillRepository>();



            builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
            builder.Services.AddAWSService<IAmazonS3>();

            builder.Services.Configure<S3Options>(builder.Configuration.GetSection("S3"));
            builder.Services.Configure<UploadLimitsOptions>(builder.Configuration.GetSection("UploadLimits"));

            builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
            builder.Services.AddScoped<TemplateService>();

            builder.Services.AddScoped<INotificationService, SmtpEmailService>();


            builder.Services.AddAuthentication("Bearer")
                .AddJwtBearer("Bearer", options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = builder.Configuration["Jwt:Issuer"],
                        ValidAudience = builder.Configuration["Jwt:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
                        RoleClaimType = ClaimTypes.Role,
                        NameClaimType = ClaimTypes.Name
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            if (context.Request.Cookies.TryGetValue("jwt", out var token) &&
                                !string.IsNullOrWhiteSpace(token))
                            {
                                context.Token = token;
                            }
                            return Task.CompletedTask;
                        }
                    };
                });

            builder.Services.AddAuthorization();


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors(MyAllowSpecificOrigins);

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
