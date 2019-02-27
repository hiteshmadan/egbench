using App.Metrics;
using App.Metrics.Filters;
using App.Metrics.Infrastructure;
using App.Metrics.Internal;
using App.Metrics.Internal.NoOp;
using App.Metrics.Registry;
using App.Metrics.ReservoirSampling;
using App.Metrics.ReservoirSampling.ExponentialDecay;

namespace EGBench
{
    public class NoOpMetrics : IMetrics
    {
        private static readonly IMetricsRegistry Registry = new NullMetricsRegistry();

        public IBuildMetrics Build => new DefaultMetricsBuilderFactory(new DefaultSamplingReservoirProvider(() => new DefaultForwardDecayingReservoir()));

        public IClock Clock => new StopwatchClock();

        public IFilterMetrics Filter => new NullMetricsFilter();

        public IManageMetrics Manage => new DefaultMetricsManager(Registry);

        public IMeasureMetrics Measure => new DefaultMeasureMetricsProvider(Registry, this.Build, this.Clock);

        public IProvideMetrics Provider => new DefaultMetricsProvider(Registry, this.Build, this.Clock);

        public IProvideMetricValues Snapshot => new DefaultMetricValuesProvider(this.Filter, Registry);
    }
}
