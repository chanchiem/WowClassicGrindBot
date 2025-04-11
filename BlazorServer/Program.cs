using Core;

using Frontend;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;

using System;
using System.IO;
using System.Linq; // Added for argument parsing
using System.Threading;
using System.Threading.Tasks; // Added for Task

namespace BlazorServer;
public static class Program
{
    public static async Task Main(string[] args) // Changed to async Task
    {
        while (true)
        {
            Log.Information($"[{nameof(Program),-17}] Starting blazor server");
            CancellationTokenSource? cts = null; // Declare cts earlier
            try
            {
                var host = CreateApp(args);
                cts = host.Services.GetRequiredService<CancellationTokenSource>(); // Get CTS after host creation
                var logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger>();

                AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs args) =>
                {
                    Exception e = (Exception)args.ExceptionObject;
                    logger.LogError(e, e.Message);
                };

                // --- AutoStart Logic ---
                bool autoStart = args.Contains("--autostart");
                string? profilePath = null;
                if (autoStart)
                {
                    int profileIndex = Array.IndexOf(args, "--profile");
                    if (profileIndex != -1 && profileIndex + 1 < args.Length)
                    {
                        profilePath = args[profileIndex + 1];
                        logger.LogInformation($"[{nameof(Program),-17}] AutoStart requested with profile: {profilePath}");

                        var botController = host.Services.GetRequiredService<IBotController>();
                        var playerReader = host.Services.GetRequiredService<PlayerReader>(); // Get PlayerReader
                        // var cts = host.Services.GetRequiredService<CancellationTokenSource>(); // Removed from here

                        // Allow AddonThread time to run initially
                        logger.LogInformation($"[{nameof(Program),-17}] Allowing time for initial addon read...");
                        await Task.Delay(1500, cts.Token); // Wait 1.5 seconds initially

                        // Wait until PlayerReader has basic info (HealthMax > 0 is a good indicator)
                        logger.LogInformation($"[{nameof(Program),-17}] Confirming initial addon data received...");
                        while (playerReader.HealthMax() == 0) // Changed HealthMax to HealthMax()
                        {
                            await Task.Delay(100, cts.Token); // Check every 100ms, pass token
                            if (cts.IsCancellationRequested) // Check if app is shutting down
                            {
                                logger.LogWarning($"[{nameof(Program),-17}] AutoStart cancelled during init wait.");
                                goto EndOfLoop; // Jump out if cancelled
                            }
                        } // End of while loop
                        logger.LogInformation($"[{nameof(Program),-17}] Initial addon data received (HealthMax: {playerReader.HealthMax()}). Proceeding with AutoStart."); // Changed HealthMax to HealthMax()

                        // Explicitly trigger InitState sequence
                        logger.LogInformation($"[{nameof(Program),-17}] Triggering InitState sequence...");
                        var addonReader = host.Services.GetRequiredService<AddonReader>();
                        var exec = host.Services.GetRequiredService<ExecGameCommand>();
                        var addonConfigurator = host.Services.GetRequiredService<AddonConfigurator>();

                        addonReader.FullReset();
                        exec.Run(""); // Assuming this clears any existing command/input
                        exec.Run($"/{addonConfigurator.Config.CommandFlush}");
                        await Task.Delay(500, cts.Token); // Short delay after flush command
                        logger.LogInformation($"[{nameof(Program),-17}] InitState sequence completed.");

                        try // Start try block for profile load and bot start
                        {
                            // Assuming BotController has methods like these.
                            // Assuming BotController has a method like LoadClassProfile.
                            // Loading the class profile should implicitly load the path defined within it.
                            // Assuming this method is synchronous.
                            botController.LoadClassProfile(profilePath);
                            await Task.Delay(100, cts.Token); // Shorter delay after loading profile, pass token
                            botController.ToggleBotStatus(); // Assumes this starts the bot
                            logger.LogInformation($"[{nameof(Program),-17}] AutoStart: Bot started with class profile {profilePath}");
                        }
                        catch(Exception startEx)
                        {
                             logger.LogError(startEx, $"[{nameof(Program),-17}] AutoStart failed: {startEx.Message}");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"[{nameof(Program),-17}] AutoStart requested but --profile argument missing or invalid.");
                    }
                }
                // --- End AutoStart Logic ---

                await host.RunAsync(cts!.Token); // Use RunAsync with cancellation token (added null-forgiving operator)
            }
            catch (OperationCanceledException)
            {
                Log.Information($"[{nameof(Program),-17}] Application host run cancelled.");
            }
            catch (Exception ex)
            {
                Log.Information($"[{nameof(Program),-17}] {ex.Message}");
                Log.Information("");

                Thread.Sleep(3000);
            }

            EndOfLoop:; // Label for goto statement
        }
    }

    private static WebApplication CreateApp(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddSerilog();

        ConfigureServices(builder.Configuration, builder.Services);

        return ConfigureApp(builder, builder.Environment);
    }

    private static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        ILoggerFactory logFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders().AddSerilog();
        });

        services.AddLogging(builder =>
        {
            LoggerSink sink = new();
            builder.Services.AddSingleton(sink);

            const string outputTemplate = "[{@t:HH:mm:ss:fff} {@l:u1}] {#if Length(SourceContext) > 0}[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),-17}] {#end}{@m}\n{@x}";
            //const string outputTemplate = "[{@t:HH:mm:ss:fff} {@l:u1}] {SourceContext}] {@m}\n{@x}";

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .WriteTo.Sink(sink)
                .WriteTo.File(new ExpressionTemplate(outputTemplate),
                    "out.log",
                    rollingInterval: RollingInterval.Day)
                .WriteTo.Debug(new ExpressionTemplate(outputTemplate))
                .WriteTo.Console(new ExpressionTemplate(outputTemplate, theme: TemplateTheme.Literate))
                .CreateLogger();

            builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(logFactory.CreateLogger(string.Empty));
        });

        Microsoft.Extensions.Logging.ILogger log = logFactory.CreateLogger("Program");

        log.LogInformation(
            $"{Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName} " +
            $"{DateTimeOffset.Now}");

        services.AddStartupConfigurations(configuration);

        services.AddWoWProcess(log);

        services.AddCoreBase();

        if (AddonConfig.Exists() && FrameConfig.Exists())
        {
            services.AddCoreNormal(log);
        }
        else
        {
            services.AddCoreConfiguration(log);
        }

        services.AddFrontend();

        services.AddCoreFrontend();

        services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static WebApplication ConfigureApp(WebApplicationBuilder builder, IWebHostEnvironment env)
    {
        WebApplication app = builder.Build();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();

        DataConfig dataConfig = app.Services.GetRequiredService<DataConfig>();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, dataConfig.Path)),
            RequestPath = "/path"
        });

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(Frontend._Imports).Assembly);

        app.UseAntiforgery();

        return app;
    }

}
