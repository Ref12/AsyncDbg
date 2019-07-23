using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using AsyncCausalityDebugger;
using AsyncDbg.Causality;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.WpfGraphControl;
using Color = Microsoft.Msagl.Drawing.Color;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Size = System.Windows.Size;

namespace AsyncCausalityViewer
{
    /// <summary>
    /// Interaction logic for GraphViewerControl.xaml
    /// </summary>
    public partial class GraphViewerControl : UserControl
    {
        GraphViewer graphViewer = new GraphViewer();

        public GraphViewerControl()
        {
            InitializeComponent();
            graphViewer.BindToPanel(graphViewerPanel);
        }

        public void LoadFromCausalityContext(CausalityContext context)
        {
            Graph graph = new Graph();

            foreach (var node in context.Nodes)
            {
                if (node.Dependencies.Count == 0 && node.Dependents.Count == 0)
                {
                    continue;
                }

                var graphNode = graph.AddNode(node.Id);
                graphNode.LabelText = node.CreateDisplayText();

                foreach (var dependency in node.Dependencies)
                {
                    graph.AddEdge(
                        source: node.Id,
                        target: dependency.Id);
                }
            }

            graph.LayoutAlgorithmSettings.PackingMethod = Microsoft.Msagl.Core.Layout.PackingMethod.Columns;
            //graph.Attr.LayerDirection = LayerDirection.None;
            graph.Attr.AspectRatio = 1;
            graphViewer.Graph = graph;
        }

        public void CreateAndLayoutAndDisplayGraph()
        {
            try
            {
                Graph graph = new Graph();
                graph.AddEdge("47", "58");
                graph.AddEdge("70", "71");

                var subgraph = new Subgraph("subgraph1");
                graph.RootSubgraph.AddSubgraph(subgraph);
                subgraph.AddNode(graph.FindNode("47"));
                subgraph.AddNode(graph.FindNode("58"));

                var subgraph2 = new Subgraph("subgraph2");
                subgraph2.Attr.Color = Color.Black;
                subgraph2.Attr.FillColor = Color.Yellow;
                subgraph2.AddNode(graph.FindNode("70"));
                subgraph2.AddNode(graph.FindNode("71"));
                subgraph.AddSubgraph(subgraph2);
                graph.AddEdge("58", subgraph2.Id);
                graph.Attr.LayerDirection = LayerDirection.LR;
                graphViewer.Graph = graph;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
