using DatabaseWebAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// 加载配置文件
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("config.json")
    .Build();

// 创建 Web 应用构建器
var builder = WebApplication.CreateBuilder(args);

// 配置服务
builder.Services.AddCors(options =>
{
    // 配置跨域资源共享（CORS）
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// 配置数据库上下文
builder.Services.AddDbContext<OracleDbContext>(options =>
{
    options.UseOracle("User Id="
                      + config["DatabaseConfig:UserId"]
                      + ";Password="
                      + config["DatabaseConfig:Password"]
                      + ";Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST="
                      + config["DatabaseConfig:Host"]
                      + ")(PORT="
                      + config["DatabaseConfig:Port"]
                      + ")))(CONNECT_DATA=(SERVICE_NAME="
                      + config["DatabaseConfig:ServiceName"] + ")));");
});

// 配置全局请求体大小限制
builder.Services.Configure<KestrelServerOptions>(options => { options.Limits.MaxRequestBodySize = null; });

// 添加服务到容器
builder.Services.AddControllers();

// 构建 Web 应用
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // 启用开发者异常页面中间件
}
app.UseCors("AllowAll"); // 启用跨域资源共享（CORS）
app.UseRouting(); // 启用路由中间件
app.UseHttpsRedirection(); // 启用 HTTPS 重定向中间件
app.UseAuthorization(); // 启用授权中间件
app.MapControllers(); // 将控制器映射到路由
app.Run(); // 启动应用程序并开始处理请求