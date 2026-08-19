using LineReservation.Models;
using LineReservation.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LineLoginOptions>(
    builder.Configuration.GetSection(LineLoginOptions.SectionName));

builder.Services.AddSingleton<Func_Log>();
builder.Services.AddHttpClient<LineLoginService>();
builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".LineReservation.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

// 僅在「網站根目錄不是 /LineReservation，但公開網址包含它」時取消註解
// app.UsePathBase("/LineReservation");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
