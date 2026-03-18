using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    [Serializable]
    public class StoryFlowDialogueState
    {
        public string NodeId;
        public string Title;
        public string Text;
        public Sprite Image;
        public AudioClip Audio;
        public StoryFlowCharacterData Character;
        public List<StoryFlowTextBlock> TextBlocks;
        public List<StoryFlowOption> Options;
        public bool IsValid;
        public bool CanAdvance;
        public bool AudioLoop;
        public bool AudioReset;
        public bool AudioAdvanceOnEnd;
        public bool AudioAllowSkip;

        public StoryFlowDialogueState()
        {
            TextBlocks = new List<StoryFlowTextBlock>();
            Options = new List<StoryFlowOption>();
            Character = new StoryFlowCharacterData();
        }
    }

    [Serializable]
    public class StoryFlowOption
    {
        public string Id;
        public string Text;
        public bool IsOnceOnly;
        public bool IsSelected;
        public string InputType;
        public string DefaultValue;
    }

    [Serializable]
    public class StoryFlowTextBlock
    {
        public string Id;
        public string Text;
    }

    [Serializable]
    public class StoryFlowFlowDef
    {
        public string Id;
        public string Name;
        public string EntryNodeId;
        public bool IsExit;
    }

    [Serializable]
    public class StoryFlowAsset
    {
        public string Id;
        public string Path;
        public string Type;
    }
}
