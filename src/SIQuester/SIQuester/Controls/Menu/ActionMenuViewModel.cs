﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace SIQuester.ViewModel
{
    /// <summary>
    /// Класс, управляющий меню действий
    /// </summary>
    public sealed class ActionMenuViewModel: DependencyObject
    {
        public static ActionMenuViewModel Instance { get; private set; }

        static ActionMenuViewModel()
        {
            Instance = new ActionMenuViewModel();
        }

        private ActionMenuViewModel()
        {

        }
        
        public bool IsOpen
        {
            get { return (bool)GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsOpen.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register("IsOpen", typeof(bool), typeof(ActionMenuViewModel), new UIPropertyMetadata(false));

        public UIElement PlacementTarget
        {
            get { return (UIElement)GetValue(PlacementTargetProperty); }
            set { SetValue(PlacementTargetProperty, value); }
        }

        // Using a DependencyProperty as the backing store for PlacementTarget.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PlacementTargetProperty =
            DependencyProperty.Register("PlacementTarget", typeof(UIElement), typeof(ActionMenuViewModel), new UIPropertyMetadata(null));
    }
}
