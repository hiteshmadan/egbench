// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using App.Metrics;
using App.Metrics.Apdex;
using App.Metrics.Counter;
using App.Metrics.Filters;
using App.Metrics.Gauge;
using App.Metrics.Histogram;
using App.Metrics.Meter;
using App.Metrics.Timer;

namespace EGBench
{
    internal class NonZeroMetricsFilter : IFilterMetrics
    {
        public bool IsApdexMatch(ApdexValueSource apdex) => true;

        public bool IsContextMatch(string context) => true;

        public bool IsCounterMatch(CounterValueSource counter) => counter.Value.Count > 0;

        public bool IsGaugeMatch(GaugeValueSource gauge) => gauge.Value > 0;

        public bool IsHistogramMatch(HistogramValueSource histogram)
        {
            HistogramValue value = histogram.Value;
            return value.SampleSize > 0 && value.Max > 0;
        }

        public bool IsMeterMatch(MeterValueSource meter) => true;

        public bool IsTimerMatch(TimerValueSource timer) => true;

        public IFilterMetrics WhereContext(Predicate<string> condition) => this;

        public IFilterMetrics WhereContext(string context) => this;

        public IFilterMetrics WhereName(string name) => this;

        public IFilterMetrics WhereName(Predicate<string> condition) => this;

        public IFilterMetrics WhereNameStartsWith(string name) => this;

        public IFilterMetrics WhereTaggedWithKey(params string[] tagKeys) => this;

        public IFilterMetrics WhereTaggedWithKeyValue(TagKeyValueFilter tags) => this;

        public IFilterMetrics WhereType(params MetricType[] types) => this;
    }
}
