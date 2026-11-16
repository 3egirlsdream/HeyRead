using HeyRead.Core;
namespace HeyRead.Desktop.ViewModels;

public static class ADesignStories
{
    private static readonly string Intro = "时隔多年再次回到苇名，依然会被《只狼：影逝二度》那种独特的战斗节奏所吸引。它不靠数值堆叠，也不提供复杂的技能树，而是用“招架—反击—压制”的简单循环，构建出一套紧张、清晰且极具张力的战斗语言。";
    private static readonly string Body = "《只狼》的战斗并非单纯的“攻与防”，而更像是一场与敌人的对话。敌人的每一次挥刀、突刺、招架，都在向玩家传递节奏与意图。招架的瞬间，既是对时机的判断，也是对手动动作的回应。随着交锋的深入，战斗逐渐从小心试探变为相互读招的博弈，这也正是它令人着迷的地方。";
    public static readonly Story[] Items = [
        new("design-1", Source.DEMO, "重返苇名：在《只狼》中读懂战斗的节奏", Intro, Author: "山海游戏志", Date: "今天 10:30", Image: "design:1", Profile: new("", "山海游戏志", "design:avatar"), Blocks: [new(BlockKind.Paragraph, Intro), new(BlockKind.Heading, "每一次招架，都是一次对话", Level: 2), new(BlockKind.Paragraph, Body), new(BlockKind.Game, "只狼：影逝二度", "design:game", Caption: "动作 / 冒险 / FromSoftware", AppId: 814380), new(BlockKind.Quote, Children:[new(BlockKind.Paragraph, "真正的胜利，不是击败敌人，而是读懂他的节奏，并在同样的节奏中超越他。")])], CommentCount: 36, ReadMinutes: 8, HeroImage: "design:hero"),
        new("design-2", Source.DEMO, "独立游戏里的秋日散步", "走进一个安静的秋日世界。", Author: "独立游戏笔记", Date: "昨天 20:14", Image: "design:2", CommentCount: 24),
        new("design-3", Source.DEMO, "本周 Steam 值得关注的更新", "游戏更新与社区公告。", Author: "Steam 周报", Date: "11-14 18:42", Image: "design:3", CommentCount: 58),
        new("design-4", Source.DEMO, "当开放世界开始讲述小故事", "属于每一位探索者的故事。", Author: "游戏叙事所", Date: "11-13 16:27", Image: "design:4", CommentCount: 41),
        new("design-5", Source.DEMO, "从像素到光影：一座城市的诞生", "街角与城市的光影。", Author: "像素研究室", Date: "11-12 11:06", Image: "design:5", CommentCount: 19),
        new("design-6", Source.DEMO, "太空、生存与建造：科幻游戏的下一站", "下一段星际旅程。", Author: "星际游戏社", Date: "11-11 09:20", Image: "design:6", CommentCount: 27)
    ];
}
