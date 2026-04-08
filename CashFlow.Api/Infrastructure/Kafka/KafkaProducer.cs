using Confluent.Kafka;
using System.Text.Json;

public class KafkaProducer : IKafkaProducer
{
    private readonly IProducer<Null, string> _producer;

    public KafkaProducer(IConfiguration config)
    {
        var conf = new ProducerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"]
        };

        _producer = new ProducerBuilder<Null, string>(conf).Build();
    }

    public async Task PublishAsync(string topic, object message)
    {
        var json = JsonSerializer.Serialize(message);

        await _producer.ProduceAsync(topic, new Message<Null, string>
        {
            Value = json
        });
    }

    public Task Publicar (string topico, string mensagem)
    {
        //implementação Real
        return Task.CompletedTask;
    }
}