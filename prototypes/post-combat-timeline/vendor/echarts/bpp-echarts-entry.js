// Minimal offline ECharts surface for the combat report.
import * as echarts from "echarts/core";
import { BarChart, CustomChart, LineChart, ScatterChart } from "echarts/charts";
import { GridComponent, MarkLineComponent, TooltipComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";

echarts.use([
  CanvasRenderer,
  BarChart,
  CustomChart,
  LineChart,
  ScatterChart,
  GridComponent,
  TooltipComponent,
  MarkLineComponent,
]);

window.echarts = echarts;
