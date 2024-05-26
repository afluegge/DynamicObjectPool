using System.Diagnostics;
using FluentAssertions;
using Haisl.Utils;

namespace DynamicObjectPool.UnitTests
{
    public class StopwatchWrapperTests
    {
        [Fact]
        public void Start_ShouldStartStopwatch()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());

            // Act
            stopwatchWrapper.Start();

            // Assert
            stopwatchWrapper.IsRunning.Should().BeTrue();
        }

        [Fact]
        public void Stop_ShouldStopStopwatch()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());
            stopwatchWrapper.Start();

            // Act
            stopwatchWrapper.Stop();

            // Assert
            stopwatchWrapper.IsRunning.Should().BeFalse();
        }

        [Fact]
        public async Task Reset_ShouldResetStopwatch()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());
            stopwatchWrapper.Start();

            await Task.Delay(10);

            stopwatchWrapper.Stop();

            // Act
            var elapsedMillisecondsBeforeReset = stopwatchWrapper.ElapsedMilliseconds;

            stopwatchWrapper.Reset();

            // Assert
            elapsedMillisecondsBeforeReset.Should().BeGreaterThan(0);
            stopwatchWrapper.ElapsedMilliseconds.Should().Be(0);
        }

        [Fact]
        public async Task Restart_ShouldRestartStopwatch()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());
            stopwatchWrapper.Start();

            await Task.Delay(50);

            stopwatchWrapper.Stop();

            // Act
            var elapsedMillisecondsBeforeRestart = stopwatchWrapper.ElapsedMilliseconds;
            stopwatchWrapper.Restart();

            await Task.Delay(5);

            // Assert
            stopwatchWrapper.IsRunning.Should().BeTrue();
            stopwatchWrapper.ElapsedMilliseconds.Should().BeLessThan(elapsedMillisecondsBeforeRestart);
        }


        [Fact]
        public async Task ElapsedTicks_ShouldReturnIncreasingValue()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());

            // Act
            stopwatchWrapper.Start();

            var elapsedTicks1 = stopwatchWrapper.ElapsedTicks;

            await Task.Delay(10);

            var elapsedTicks2 = stopwatchWrapper.ElapsedTicks;

            // Assert
            stopwatchWrapper.IsRunning.Should().BeTrue();
            elapsedTicks2.Should().BeGreaterThan(elapsedTicks1);
        }

        [Fact]
        public async Task ElapsedMilliseconds_ShouldReturnIncreasingValue()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());

            // Act
            stopwatchWrapper.Start();

            var elapsedMilliseconds1 = stopwatchWrapper.ElapsedMilliseconds;

            await Task.Delay(10);

            var elapsedMilliseconds2 = stopwatchWrapper.ElapsedMilliseconds;

            // Assert
            stopwatchWrapper.IsRunning.Should().BeTrue();
            elapsedMilliseconds2.Should().BeGreaterThan(elapsedMilliseconds1);
        }


        [Fact]
        public async Task Elapsed_ShouldReturnIncreasingTimeSpan()
        {
            // Arrange
            var stopwatchWrapper = new StopwatchWrapper(new Stopwatch());

            // Act
            stopwatchWrapper.Start();

            var elapsedTimeSpan1 = stopwatchWrapper.Elapsed;

            await Task.Delay(10);

            var elapsedTimeSpan2 = stopwatchWrapper.Elapsed;

            // Assert
            stopwatchWrapper.IsRunning.Should().BeTrue();
            elapsedTimeSpan2.Should().BeGreaterThan(elapsedTimeSpan1);
        }
    }
}
