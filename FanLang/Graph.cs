using System;
using System.Collections.Generic;

// 节点对象
class Node
{
    public int val;
    public List<Node> dependencies;
    public Node(int val)
    {
        this.val = val;
        this.dependencies = new List<Node>();
    }
}

class TopologicalSort
{
    // 拓扑排序函数
    public static List<int> TopoSort(Node[] nodes)
    {
        // 记录每个节点的入度
        Dictionary<Node, int> inDegrees = new Dictionary<Node, int>();
        foreach (Node node in nodes)
        {
            foreach (Node dependencyNode in node.dependencies)
            {
                if (!inDegrees.ContainsKey(dependencyNode))
                {
                    inDegrees[dependencyNode] = 0;
                }
                inDegrees[dependencyNode]++;
            }
        }

        // 拓扑排序（逐个删除入度0节点来实现排序）  
        Queue<Node> queue = new Queue<Node>();
        foreach (Node node in nodes)
        {
            if (!inDegrees.ContainsKey(node))
            {
                queue.Enqueue(node);
            }
        }
        List<int> result = new List<int>();
        while (queue.Count > 0)
        {
            Node node = queue.Dequeue();
            result.Add(node.val);
            foreach (Node dependencyNode in node.dependencies)
            {
                inDegrees[dependencyNode]--;
                if (inDegrees[dependencyNode] == 0)
                {
                    queue.Enqueue(dependencyNode);
                }
            }
        }

        // 检查是否有环路
        if (result.Count != nodes.Length)
        {
            throw new InvalidOperationException("图中存在环路，无法进行拓扑排序！");
        }

        return result;
    }
}
