public interface IKafkaProducer
{
    Task Publicar(string topico, string mensagem);

    public Task PublishAsync(string topic, object message)
    {
        // implementação real
        return Task.CompletedTask;
    }
}