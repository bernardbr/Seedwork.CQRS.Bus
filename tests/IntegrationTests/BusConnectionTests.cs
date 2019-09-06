using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Seedwork.CQRS.Bus.Core;
using Xunit;

namespace Seedwork.CQRS.Bus.IntegrationTests
{
    public class BusConnectionTests : IClassFixture<BusConnectionFixture>
    {
        public BusConnectionTests(BusConnectionFixture connectionFixture)
        {
            _connectionFixture = connectionFixture;
        }

        private readonly BusConnectionFixture _connectionFixture;

        [Fact]
        public async Task GivenConnectionWhenPublishBatchShouldQueueHaveBatchLength()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";
            await _connectionFixture.Connection.PublishBatch(exchange, queue, routingKey, new[]
            {
                notification,
                notification
            });

            _connectionFixture.Connection.MessageCount(queue).Should().Be(2);
        }

        [Fact]
        public async Task GivenConnectionWhenPublishMessageShouldQueueHaveCountOne()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";
            await _connectionFixture.Connection.Publish(exchange, queue, routingKey, notification);

            _connectionFixture.Connection.MessageCount(queue).Should().Be(1);
        }

        [Fact]
        public async Task GivenConnectionWhenPublishShouldAddRetryProperties()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            object data = "Notification message";
            var message = Message.Create(data, 10);

            await _connectionFixture.Connection.Publish(exchange, queue, routingKey, message);

            var responseMessage = _connectionFixture.Connection.GetMessage(queue);
            responseMessage.Should().NotBeNull();
            responseMessage.Data.Should().Be(data);
            responseMessage.AttemptCount.Should().Be(1);
            responseMessage.MaxAttempts.Should().Be(10);
        }

        [Fact]
        public async Task GivenConnectionWhenSubscribeAndThrowShouldRequeueOnRetryQueue()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}");
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";

            await _connectionFixture.Connection.Publish(exchange, queue, routingKey, notification);

            var @event = new AutoResetEvent(false);

            _connectionFixture.Connection.Subscribe<string>(exchange, queue, routingKey, 1, (scope, message) =>
            {
                Task.Run(() =>
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(500));
                    @event.Set();
                });
                throw new Exception();
            });

            @event.WaitOne();

            var retryQueue = Queue.Create($"{queue.Name.Value}-retry");

            _connectionFixture.Connection.MessageCount(retryQueue).Should().Be(1);
        }

        [Fact]
        public async Task GivenConnectionWhenSubscribeShouldExecuteCallback()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";

            await _connectionFixture.Connection.Publish(exchange, queue, routingKey, notification);

            IServiceScope callbackScope = null;
            string callbackMessage = null;

            var @event = new AutoResetEvent(false);

            _connectionFixture.Connection.Subscribe<string>(exchange, queue, routingKey, 1, (scope, message) =>
            {
                callbackScope = scope;
                callbackMessage = message;
                @event.Set();

                return Task.CompletedTask;
            });

            @event.WaitOne();

            callbackScope.Should().NotBeNull();
            callbackMessage.Should().Be(notification);
        }
    }
}