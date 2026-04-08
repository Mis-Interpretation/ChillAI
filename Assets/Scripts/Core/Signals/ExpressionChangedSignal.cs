namespace ChillAI.Core.Signals
{
    public class ExpressionChangedSignal
    {
        public ExpressionType Expression { get; }

        public ExpressionChangedSignal(ExpressionType expression)
        {
            Expression = expression;
        }
    }
}
