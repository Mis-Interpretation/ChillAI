using ChillAI.Core;

namespace ChillAI.Model.Expression
{
    public interface IExpressionStateWriter : IExpressionStateReader
    {
        void SetExpression(ExpressionType expression);
    }
}
