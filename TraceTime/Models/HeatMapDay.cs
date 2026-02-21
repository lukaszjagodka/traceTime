using System;
using System.Collections.Generic;
using System.Text;

namespace TraceTime.Models
{
    public class HeatMapDay
    {
        public DateTime Date { get; set; }
        public double Hours { get; set; }
        public string Color { get; set; } = "#212121";
        public string ToolTipText { get; set; } = string.Empty;
    }
}