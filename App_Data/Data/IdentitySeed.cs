using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebLibrary.App.Data
{
    public static class IdentitySeed
    {
        // Permite sobrescribir por variables de entorno si existen
        private static string EnvOr(string key, string fallback) =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))
                ? fallback
                : Environment.GetEnvironmentVariable(key)!;

        public static async Task EnsureSeedAsync(IServiceProvider services)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeed");
            var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userMgr = services.GetRequiredService<UserManager<IdentityUser>>();

            string[] roles = { "Admin", "Secretario", "Tecnico" };
            foreach (var r in roles)
            {
                if (!await roleMgr.RoleExistsAsync(r))
                {
                    var rRes = await roleMgr.CreateAsync(new IdentityRole(r));
                    if (!rRes.Succeeded) logger.LogWarning("No se creó rol {Role}: {Err}", r, string.Join("; ", rRes.Errors));
                }
            }

            // Admin
            await EnsureUserAsync(
                userMgr, logger,
                username: "admin@local",
                password: EnvOr("ADMIN_PASSWORD", "Admin#12345"),
                rolesToAdd: new[] { "Admin" }
            );

            // Secretario
            await EnsureUserAsync(
                userMgr, logger,
                username: "secretario@local",
                password: EnvOr("SECRETARIO_PASSWORD", "Secretario#12345"),
                rolesToAdd: new[] { "Secretario" }
            );

            // Tecnico
            await EnsureUserAsync(
                userMgr, logger,
                username: "tecnico@local",
                password: EnvOr("TECNICO_PASSWORD", "Tecnico#12345"),
                rolesToAdd: new[] { "Tecnico" }
            );
        }

        private static async Task EnsureUserAsync(
            UserManager<IdentityUser> userMgr,
            ILogger logger,
            string username,
            string password,
            string[] rolesToAdd)
        {
            var user = await userMgr.FindByNameAsync(username);
            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = username,
                    Email = username,
                    EmailConfirmed = true
                };
                var createRes = await userMgr.CreateAsync(user, password);
                if (!createRes.Succeeded)
                {
                    logger.LogWarning("No se creó usuario {User}: {Err}", username, string.Join("; ", createRes.Errors));
                    return;
                }
            }

            foreach (var role in rolesToAdd)
            {
                if (!await userMgr.IsInRoleAsync(user, role))
                {
                    var addRes = await userMgr.AddToRoleAsync(user, role);
                    if (!addRes.Succeeded)
                        logger.LogWarning("No se asignó rol {Role} a {User}: {Err}", role, username, string.Join("; ", addRes.Errors));
                }
            }
        }
    }
}
