using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;


namespace Gizbox.Src.Backend
{
    public static class x64Target
    {
        public static void CodeGen(ILUnit ir)
        {
            x64CodeGenContext context = new x64CodeGenContext(ir);

            context.StartCodeGen();
        }
    }


    public class BasicBlock
    {
        public int startIdx;
        public int endIdx;
    }

    public class x64CodeGenContext
    {
        private ILUnit ir;

        private List<BasicBlock> blocks;

        public x64CodeGenContext(ILUnit ir)
        {
            this.ir = ir;
        }

        public void StartCodeGen()
        {
            Pass1();
        }


        /// <summary> 划分基本块 </summary>
        private void Pass1()
        {
            blocks = new List<BasicBlock>();

            int blockstart = 0;
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];

                if(tac.op == "IF_FALSE_JUMP" ||
                    tac.op == "JUMP" ||
                    string.IsNullOrEmpty(tac.label) == false
                    )
                {
                    //Block End
                    BasicBlock b = new BasicBlock() { startIdx = blockstart, endIdx = i };
                    blocks.Add(b);
                    blockstart = i + 1;
                }
            }


            Gizbox.Debug.Log("Blocks:" + string.Join("\n  Block->", blocks.Select(b => ir.codes[b.startIdx].ToExpression() + "     ---->      " + ir.codes[b.endIdx].ToExpression())));
        }
    }
}
