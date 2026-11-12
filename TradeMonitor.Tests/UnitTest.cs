using NUnit.Framework;
using ServiceStack;
using ServiceStack.Testing;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

public class UnitTest
{
    private readonly ServiceStackHost appHost;

    public UnitTest()
    {
        appHost = new BasicAppHost().Init();
        appHost.Container.AddTransient<MyServices>();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => appHost.Dispose();

    [Test]
    public void Can_call_MyServices()
    {
        var service = appHost.Container.Resolve<MyServices>();

        var response = (HelloResponse)service.Any(new Hello { Name = "World" });

        Assert.That(response.Result, Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void Service_model_has_no_template_demo_types()
    {
        var names = new HashSet<string>(typeof(Hello).Assembly.GetTypes().Select(t => t.Name));

        Assert.That(names, Does.Contain(nameof(Hello)));
        Assert.That(names, Does.Not.Contain("Booking"));
        Assert.That(names, Does.Not.Contain("Todo"));
        Assert.That(names, Does.Not.Contain("QueryTodos"));
        Assert.That(names, Does.Not.Contain("QueryBookings"));
        Assert.That(names, Does.Not.Contain("GetWeatherForecast"));
    }
}