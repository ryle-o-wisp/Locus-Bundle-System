using UnityEngine;

namespace BundleSystem
{
    public sealed class InstanceAsyncOperator : CustomYieldInstruction
    {
        private bool _keepWaiting = true;
        public override bool keepWaiting => _keepWaiting;

        private void SetComplete()
        {
            _keepWaiting = false;
        }

        public static readonly InstanceAsyncOperator Done = new InstanceAsyncOperator { _keepWaiting = false };
        
        public sealed class ConfirmationHandler
        {
            public readonly InstanceAsyncOperator Operator;
            private ConfirmationHandler(InstanceAsyncOperator @operator)
            {
                Operator = @operator;
            }

            public void SetComplete()
            {
                Operator.SetComplete();
            }
            
            public static ConfirmationHandler Create()
            {
                return new ConfirmationHandler(new InstanceAsyncOperator());
            }
        }
    }
    
    
    public sealed class InstanceAsyncOperator<T> : CustomYieldInstruction
    {
        private bool _keepWaiting = true;
        public override bool keepWaiting => _keepWaiting;
        public T Result { get; private set; }
        public bool IsFinished => _keepWaiting == false;
        
        private void Complete(T result)
        {
            _keepWaiting = false;
            Result = result;
        }

        public static InstanceAsyncOperator<T> CreateCompleted(T result)
        {
            var resultOp = new InstanceAsyncOperator<T> { _keepWaiting = false };
            resultOp.Complete(result);
            return resultOp;
        }
        
        public static readonly InstanceAsyncOperator<T> Done = new InstanceAsyncOperator<T> { _keepWaiting = false };
        
        public sealed class ConfirmationHandler
        {
            public readonly InstanceAsyncOperator<T> Operator;
            private ConfirmationHandler(InstanceAsyncOperator<T> @operator)
            {
                Operator = @operator;
            }

            public void Complete(T result)
            {
                Operator.Complete(result);
            }
            
            public static ConfirmationHandler Create()
            {
                return new ConfirmationHandler(new InstanceAsyncOperator<T>());
            }
        }
    }
    
    
    public sealed class InstanceAsyncOperator<TResult,TError> : CustomYieldInstruction
    {
        private bool _keepWaiting = true;
        public override bool keepWaiting => _keepWaiting;
        public TResult Result { get; private set; }
        public bool IsFinished => _keepWaiting == false;
        public bool HasErrored { get; private set; }
        public TError Error { get; private set; }
        
        private void Complete(TResult result)
        {
            _keepWaiting = false;
            HasErrored = false;
            Result = result;
        }

        private void Fault(TError error)
        {
            _keepWaiting = false;
            HasErrored = true;
            Error = error;
        }

        public static InstanceAsyncOperator<TResult,TError> CreateCompleted(TResult result)
        {
            var resultOp = new InstanceAsyncOperator<TResult,TError> { _keepWaiting = false };
            resultOp.Complete(result);
            return resultOp;
        }
        
        public static readonly InstanceAsyncOperator<TResult,TError> Done = new InstanceAsyncOperator<TResult,TError> { _keepWaiting = false };
        
        public sealed class ConfirmationHandler
        {
            public readonly InstanceAsyncOperator<TResult,TError> Operator;
            private ConfirmationHandler(InstanceAsyncOperator<TResult,TError> @operator)
            {
                Operator = @operator;
            }

            public void Complete(TResult result)
            {
                Operator.Complete(result);
            }

            public void Fault(TError error)
            {
                Operator.Fault(error);
            }
            
            public static ConfirmationHandler Create()
            {
                return new ConfirmationHandler(new InstanceAsyncOperator<TResult,TError>());
            }
        }
    }
}