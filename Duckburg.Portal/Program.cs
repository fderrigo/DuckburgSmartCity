using ChattyDuck.Quack;

var builder = WebApplication.CreateBuilder(args);

// Portale del Comune: Razor Pages. L'assistente ChattyDuck e' montato come widget.
builder.Services.AddRazorPages();
builder.Services.AddQuack();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRouting();

app.UseStaticFiles();
app.MapRazorPages();

// Canale chat e diagnostica dell'assistente.
app.MapQuackEndpoints();


app.Run();
