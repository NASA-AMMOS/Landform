namespace JPLOPS.Util
{
    public class DisjointSet
    {
        int[] parent;
        int[] rank; // height of tree

        public DisjointSet(int len)
        {
            parent = new int[len + 1];
            rank = new int[len + 1];
            for (int i = 0; i < len; ++i)
            {
                MakeSet(i);
            }
        }

        public void MakeSet(int i)
        {
            parent[i] = i;
        }

        // Path compression, O(log*n). For practical values of n, log* n <= 5
        public int Find(int i)
        {
            while (i != parent[i]) // If i is not root of tree we set i to his parent until we reach root (parent of all parents)
            {
                i = parent[i];
            }
            return i;
        }

        public void Union(int i, int j)
        {
            int i_id = Find(i); // Find the root of first tree (set) and store it in i_id
            int j_id = Find(j); // // Find the root of second tree (set) and store it in j_id

            if (i_id == j_id) // If roots are equal (they have same parents) than they are in same tree (set)
            {
                return;
            }

            if (rank[i_id] > rank[j_id]) // If height of first tree is larger than second tree
            {
                parent[j_id] = i_id; // We hang second tree under first, parent of second tree is same as first tree
            }
            else
            {
                parent[i_id] = j_id; // We hang first tree under second, parent of first tree is same as second tree
                if (rank[i_id] == rank[j_id]) // If heights are same
                {
                    rank[j_id]++; // We hang first tree under second, that means height of tree is incremented by one
                }
            }
        }
    }
}
