using System;
using System.Collections.Generic;

namespace SpokysProjectLightning.Services
{
    public class NavigationService
    {
        private readonly Stack<object> _history = new();
        private readonly Stack<object> _forward = new();

        public object? CurrentPage { get; private set; }

        public event Action<object>? PageChanged;

        public void Navigate(object page)
        {
            if (CurrentPage != null)
                _history.Push(CurrentPage);
            _forward.Clear();
            CurrentPage = page;
            PageChanged?.Invoke(page);
        }

        public void GoBack()
        {
            if (_history.Count > 0)
            {
                _forward.Push(CurrentPage!);
                CurrentPage = _history.Pop();
                PageChanged?.Invoke(CurrentPage!);
            }
        }

        public void GoForward()
        {
            if (_forward.Count > 0)
            {
                _history.Push(CurrentPage!);
                CurrentPage = _forward.Pop();
                PageChanged?.Invoke(CurrentPage!);
            }
        }

        public bool CanGoBack => _history.Count > 0;
        public bool CanGoForward => _forward.Count > 0;
    }
}

