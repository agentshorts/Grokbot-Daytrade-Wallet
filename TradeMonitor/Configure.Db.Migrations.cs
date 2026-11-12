using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TradeMonitor.Data;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureDbMigrations))]

namespace TradeMonitor;

// Code-First DB Migrations: https://docs.servicestack.net/ormlite/db-migrations
public class ConfigureDbMigrations : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureAppHost(appHost => {
            var dbFactory = appHost.Resolve<IDbConnectionFactory>();
            var migrator = new Migrator(dbFactory, typeof(ConfigureDbMigrations).Assembly);
            void RunMigrations()
            {
                var log = appHost.GetApplicationServices().GetRequiredService<ILogger<ConfigureDbMigrations>>();

                log.LogInformation("Running EF Migrations...");
                var scopeFactory = appHost.GetApplicationServices().GetRequiredService<IServiceScopeFactory>();
                using (var scope = scopeFactory.CreateScope())
                {
                    using var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    db.Database.EnsureCreated();
                    if (db.Database.GetPendingMigrations().Any()) {
                        log.LogInformation("Running EF Migrations...");
                        db.Database.Migrate();
                    }

                    // Only seed users if DB was just created
                    if (!db.Users.Any())
                    {
                        log.LogInformation("Adding Seed Users...");
                        AddSeedUsers(scope.ServiceProvider).Wait();
                    }
                }

                log.LogInformation("Running OrmLite Migrations...");
                migrator.Run();
            }
            AppTasks.Register("migrate", _ => RunMigrations());
            AppTasks.Register("migrate.revert", args => migrator.Revert(args[0]));
            AppTasks.Register("migrate.rerun", args => migrator.Rerun(args[0]));
            AppTasks.Run();

            // Pending OrmLite migrations apply on every start, including an existing database.
            // Migrator records each completed name and skips it on the next start, so a second
            // start leaves rows that are already stored as they are. Migration1001 adds Trade.Kind
            // only when that column is missing, and existing rows take the Fill default.
            // Identity tables and seed users are created only when the database is empty.
            var log = appHost.GetApplicationServices().GetRequiredService<ILogger<ConfigureDbMigrations>>();
            bool identitySchemaExists;
            using (var db = dbFactory.Open())
                identitySchemaExists = db.TableExists("AspNetUsers");

            if (!identitySchemaExists)
            {
                log.LogInformation("Empty database detected; bootstrapping schemas and reference data...");
                RunMigrations();
            }
            else
            {
                log.LogInformation("Running pending OrmLite migrations...");
                var pending = migrator.Run();
                if (!pending.Succeeded)
                    throw new InvalidOperationException(pending.Error?.Message ?? "OrmLite migrations failed.", pending.Error);
            }
        });

    private async Task AddSeedUsers(IServiceProvider services)
    {
        //initializing custom roles 
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        string[] allRoles = ["Admin", "Manager", "Employee"];

        void assertResult(IdentityResult result)
        {
            if (!result.Succeeded)
                throw new Exception(result.Errors.First().Description);
        }

        async Task EnsureUserAsync(ApplicationUser user, string password, string[]? roles = null)
        {
            var existingUser = await userManager.FindByEmailAsync(user.Email!);
            if (existingUser != null) return;

            await userManager!.CreateAsync(user, password);
            if (roles?.Length > 0)
            {
                var newUser = await userManager.FindByEmailAsync(user.Email!);
                assertResult(await userManager.AddToRolesAsync(user, roles));
            }
        }

        foreach (var roleName in allRoles)
        {
            var roleExist = await roleManager.RoleExistsAsync(roleName);
            if (!roleExist)
            {
                //Create the roles and seed them to the database
                assertResult(await roleManager.CreateAsync(new IdentityRole(roleName)));
            }
        }

        ApplicationUser[] users = [
            new()
            {
                DisplayName = "Test User",
                Email = "test@email.com",
                UserName = "test@email.com",
                FirstName = "Test",
                LastName = "User",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Test Employee",
                Email = "employee@email.com",
                UserName = "employee@email.com",
                FirstName = "Test",
                LastName = "Employee",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Test Manager",
                Email = "manager@email.com",
                UserName = "manager@email.com",
                FirstName = "Test",
                LastName = "Manager",
                EmailConfirmed = true,
            },
            new()
            {
                DisplayName = "Admin User",
                Email = "admin@email.com",
                UserName = "admin@email.com",
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true,
            },
        ];

        for (int i = 0; i < users.Length; i++)
        {
            var user = users[i];
            user.ProfileUrl ??= SvgCreator.CreateSvgDataUri(char.ToUpper(user.UserName![0]), 
                    bgColor:SvgCreator.GetDarkColor(i));
            var roles = user.UserName switch
            {
                "admin@email.com" => allRoles,
                "manager@email.com" => ["Manager", "Employee"],
                "employee@email.com" => ["Employee"],
                _ => null,
            };
            await EnsureUserAsync(user, "p@55wOrd", roles);
        }
    }
}
