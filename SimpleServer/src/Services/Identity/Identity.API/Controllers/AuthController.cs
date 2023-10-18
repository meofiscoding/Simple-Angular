using System.Linq;
using System.Threading.Tasks;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Identity.API.Models.Auth;
using Identity.API.Entity;
using Microsoft.AspNetCore.Authorization;
using Identity.API.Utils;
using Microsoft.AspNetCore.Authentication;
using IdentityServer4.Stores;

namespace Identity.API.Controllers;

[AllowAnonymous]
public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interactionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IClientStore _clientStore;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interactionService,
        UserManager<ApplicationUser> userManager,
        IAuthenticationSchemeProvider schemeProvider,
        IClientStore clientStore)
    {
        _signInManager = signInManager;
        _interactionService = interactionService;
        _userManager = userManager;
        _schemeProvider = schemeProvider;
        _clientStore = clientStore;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string returnUrl)
    {
        if (TempData["ErrorMessage"] != null)
        {
            ViewBag.ErrorMessage = TempData["ErrorMessage"];
        }

        // build a model so we know what to show on the login page
        var vm = await BuildLoginViewModelAsync(returnUrl);

        if (vm.IsExternalLoginOnly)
        {
            // we only have one option for logging in and it's an external provider
            return RedirectToAction("Challenge", "External", new { scheme = vm.ExternalLoginScheme, returnUrl });
        }
        return View(vm);
    }

    private async Task<LoginViewModel> BuildLoginViewModelAsync(string returnUrl)
    {
        var context = await _interactionService.GetAuthorizationContextAsync(returnUrl);
        if (context?.IdP != null && await _schemeProvider.GetSchemeAsync(context.IdP) != null)
        {
            var local = context.IdP == IdentityServer4.IdentityServerConstants.LocalIdentityProvider;

            // this is meant to short circuit the UI and only trigger the one external IdP
            var vm = new LoginViewModel
            {
                EnableLocalLogin = local,
                ReturnUrl = returnUrl,
                Username = context?.LoginHint,
            };

            if (!local)
            {
                vm.ExternalProviders = new[] { new ExternalProvider { AuthenticationScheme = context.IdP } };
            }

            return vm;
        }

        var schemes = await _schemeProvider.GetAllSchemesAsync();

        var providers = schemes
            .Where(x => x.DisplayName != null)
            .Select(x => new ExternalProvider
            {
                DisplayName = x.DisplayName ?? x.Name,
                AuthenticationScheme = x.Name
            }).ToList();

        var allowLocal = true;
        if (context?.Client.ClientId != null)
        {
            var client = await _clientStore.FindEnabledClientByIdAsync(context.Client.ClientId);
            if (client != null)
            {
                allowLocal = client.EnableLocalLogin;

                if (client.IdentityProviderRestrictions?.Any() == true)
                {
                    providers = providers.Where(provider => client.IdentityProviderRestrictions.Contains(provider.AuthenticationScheme)).ToList();
                }
            }
        }

        return new LoginViewModel
        {
            AllowRememberLogin = AccountOptions.AllowRememberLogin,
            EnableLocalLogin = allowLocal && AccountOptions.AllowLocalLogin,
            ReturnUrl = returnUrl,
            Username = context?.LoginHint ?? string.Empty,
            ExternalProviders = providers.ToArray()
        };
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        // get tenant info// check if the model is valid
        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByNameAsync(vm.Username!) ?? await _userManager.FindByEmailAsync(vm.Username!);
            // check if the user exists
            if (user != null)
            {
                // check if the password is correct
                var signInResult = _signInManager.PasswordSignInAsync(user, vm.Password, false, false).Result;
                if (signInResult.Succeeded)
                {
                    // redirect to the return url
                    if (vm.ReturnUrl != null)
                    {
                        return Redirect(vm.ReturnUrl);
                    }
                    else
                    {
                        return View();
                    }
                }
            }
            else
            {
                ModelState.AddModelError("", "Username or password is incorrect");
            }
        }
        return Redirect(vm.ReturnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Logout(string logoutId)
    {
        await _signInManager.SignOutAsync();

        var logoutRequest = await _interactionService.GetLogoutContextAsync(logoutId);

        if (string.IsNullOrEmpty(logoutRequest.PostLogoutRedirectUri))
        {
            return RedirectToAction("Index", "Home");
        }

        return Redirect(logoutRequest.PostLogoutRedirectUri);
    }

    [HttpGet]
    public IActionResult Register(string returnUrl)
    {
        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var userByEmail = await _userManager.FindByEmailAsync(vm.Email!);
        if (userByEmail is not null)
        {
            throw new Exception($"User with email {vm.Email} already exists.");
        }

        var user = new ApplicationUser
        {
            Email = vm.Email
        };

        var result = await _userManager.CreateAsync(user, vm.Password);
        // await _userManager.AddToRoleAsync(user, "User");

        if (!result.Succeeded)
        {
            throw new Exception($"User creation failed. Errors: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await _signInManager.SignInAsync(user, false);

        return Redirect(vm.ReturnUrl);
    }

}