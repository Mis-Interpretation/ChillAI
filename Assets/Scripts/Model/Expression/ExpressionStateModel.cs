using ChillAI.Core;

namespace ChillAI.Model.Expression
{
    public class ExpressionStateModel : IExpressionStateWriter
    {
        public ExpressionType CurrentExpression { get; private set; } = ExpressionType.Neutral;

        public void SetExpression(ExpressionType expression)
        {
            CurrentExpression = expression;
        }
    }
}
