using Confluent.Kafka;
using System.Text.Json;

public class KafkaConsumer : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;

    public KafkaConsumer(IConfiguration config, IServiceProvider sp)
    {
        _config = config;
        _serviceProvider = sp;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conf = new ConsumerConfig
        {
            BootstrapServers = _config["Kafka:BootstrapServers"],
            GroupId = "cashflow-group",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var consumer = new ConsumerBuilder<Ignore, string>(conf).Build();
        consumer.Subscribe("lancamentos");

        Task.Run(() =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);

                var payload = JsonSerializer.Deserialize<Lancamento>(result.Message.Value);

                Console.WriteLine($"Evento recebido: {payload?.Valor}");
            }
        }, stoppingToken);

        return Task.CompletedTask;
    }

    private record Lancamento(decimal Valor, string Tipo, DateTime Data);
}