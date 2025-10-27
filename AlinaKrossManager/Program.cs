using AlinaKrossManager.BackgroundServices;
using AlinaKrossManager.BuisinessLogic.Services;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Регистрация Telegram Bot
builder.Services.AddSingleton<ITelegramBotClient>(provider =>
{
	var test = Environment.GetEnvironmentVariable("TelegramBotToken");
	Console.WriteLine(test);

	var token = "8169570733:AAH6HBzf2Nv8k9XjldVZ5QMm_fRJDGz-r6g";//builder.Configuration["TelegramBot:Token"];
	return new TelegramBotClient(token);
});

builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddSingleton<TelegramService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

