// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace AsyncCausalityDebugger
{
    public sealed class DgmlWriter
    {
        public class Graph
        {
            [XmlAttribute(AttributeName = "GraphDirection")]
            public string GraphDirection { get; set; } = "BottomToTop";

            [XmlAttribute(AttributeName = "Layout")]
            public string Layout { get; set; } = "Sugiyama";

            public Node[] Nodes;
            public Link[] Links;
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        public struct Node
        {
            [XmlAttribute]
            public string Id;

            [XmlAttribute]
            public string Label;

            public Node(string id, string label)
            {
                Id = id;
                Label = label;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        public struct Link
        {
            [XmlAttribute]
            public string Source;

            [XmlAttribute]
            public string Target;

            [XmlAttribute]
            public string Label;

            public Link(string source, string target, string label)
            {
                Source = source;
                Target = target;
                Label = label;
            }
        }

        public List<Node> Nodes { get; private set; }

        public List<Link> Links { get; private set; }

        public DgmlWriter()
        {
            Nodes = new List<Node>();
            Links = new List<Link>();
        }

        public void AddNode(Node n)
        {
            Nodes.Add(n);
        }

        public void AddLink(Link l)
        {
            Links.Add(l);
        }

        public void Serialize(string xmlpath)
        {
            Graph g = new Graph();
            g.Nodes = Nodes.ToArray();
            g.Links = Links.ToArray();

            XmlRootAttribute root = new XmlRootAttribute("DirectedGraph");
            root.Namespace = "http://schemas.microsoft.com/vs/2009/dgml";
            XmlSerializer serializer = new XmlSerializer(typeof(Graph), root);
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            using (XmlWriter xmlWriter = XmlWriter.Create(xmlpath, settings))
            {
                serializer.Serialize(xmlWriter, g);
            }
        }

        public string SerializeAsString()
        {
            Graph g = new Graph();
            g.Nodes = Nodes.ToArray();
            g.Links = Links.ToArray();

            XmlRootAttribute root = new XmlRootAttribute("DirectedGraph");
            root.Namespace = "http://schemas.microsoft.com/vs/2009/dgml";
            
            XmlSerializer serializer = new XmlSerializer(typeof(Graph), root);
            
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = new UTF8Encoding(false);
            settings.ConformanceLevel = ConformanceLevel.Document;

            // Have to use a custom string writer because the default one will use UTF16
            using (var stringWriter = new Utf8StringWriter())
            {
                using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, settings))
                {
                    serializer.Serialize(xmlWriter, g);
                }

                return stringWriter.ToString();
            }
        }

        private class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
