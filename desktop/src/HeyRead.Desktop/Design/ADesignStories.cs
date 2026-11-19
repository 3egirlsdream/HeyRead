using HeyRead.Core;

namespace HeyRead.Desktop.ViewModels;

public static class ADesignStories
{
    private const string Intro = "距《只狼：影逝二度》发售已经过去了好几年，但它依然频繁出现在玩家的讨论中。有人在回忆第一次击败剑圣的兴奋，有人在分析它的战斗系统，也还有人在不断地将它与 FromSoftware 后续的作品进行比较。为什么一款没有开放世界的动作游戏，多年后依然有这么高的讨论热度？";
    private const string Body = "《只狼》的独特之处，首先在于它极为纯粹的战斗体验。看似简单的格挡、弹反和架势循环，在实际游玩中有着极高的深度与节奏感。你要真正理解敌人的动作，把握时机，并在一次次失败中提升自己的反应与判断。";
    private const string Ending = "也许正因如此，《只狼》才能在发售多年后依然让人回味。它是一款出色的动作游戏，也是一段关于坚持、技艺与成长的体验。每次重新回到苇名，我们都会听见不一样的节奏。";

    public static readonly Story[] Items = [
        new("design-1", Source.DEMO, "为什么我们还在讨论《只狼》？", Intro, Author: "风见", Date: "2024-03-10 10:24",
            Profile: new("", "风见", "design:avatar"), Image: "design:1", HeroImage: "design:hero", CommentCount: 24,
            Blocks: [
                new(BlockKind.Paragraph, Intro), new(BlockKind.Paragraph, Body),
                new(BlockKind.Paragraph, "第一次遇到苇名弦一郎时，我总想靠闪避拉开距离。真正学会等刀落下再按招架后，同一场战斗突然变了样。对手的出招仍然很快，但每一次碰撞，都给了我一个可以识别和回应的节拍。"),
                new(BlockKind.Paragraph, "游戏也把同样的节奏留给了探索。从屋檐到山道，停下来听一听远处的对话，有时比赶去下一场战斗更让人记得这座城。"),
                new(BlockKind.Paragraph, Ending)
            ]),
        Sample(2, "《艾尔登法环》DLC 或将在年内推出", "游戏观察员", 342),
        Sample(3, "《塞尔达传说：王国之泪》销量突破 2000 万", "海拉鲁通讯", 96),
        Sample(4, "《最终幻想 7 重生》PC 版配置需求公布", "SE 情报站", 210),
        Sample(5, "《怪物猎人：荒野》新实机演示：更自由的狩猎", "猎人协会", 187),
        Sample(6, "《博德之门 3》评测：CRPG 的新标杆", "RPG 研究室", 265),
        Sample(7, "《空洞骑士：丝之歌》开发进度再更新", "独立游戏简报", 73),
        Sample(8, "《赛博朋克 2077》2.1 更新现已上线", "夜之城电台", 504)
    ];

    public static readonly Comment[] Comments = [
        new("r1", "r1", "直到现在我还是觉得《只狼》的战斗很爽。弹反的手感和节奏独一无二，打到剑圣那一刻真的有种悟了的快感。", new("", "墨羽", "design:comment-1"), "2024-03-10 11:02", 128, false,
            [
                new("r1-1", "r1", "那种刀刀相接的紧张感，其他游戏很难再给到同样的体验。[cube_喜欢]", new("", "青竹", "design:reply-1"), "2024-03-10 11:40", 27, false, [], [], ReplyTo: "墨羽"),
                new("r1-2", "r1", "我后来又试了一轮 Boss Rush。[cube_doge] 熟悉招式之后，才能注意到敌人出手前那些很细小的提示。有时候连续几次弹反的节奏看起来相同，最后一下其实会比预想慢一点。多练几次就能听出来。[cube_喜欢]",
                    new("", "AshinaReader"), "2024-03-10 12:03", 0, false, [], [], ReplyTo: "青竹"),
                new("r1-3", "r1", "我也是在听清叮的一声之后才摸到节奏。", new("", "山下散步"), "2024-03-10 12:15", 3, false, [], [])
            ], []),
        new("r2", "r2", "我更喜欢它的叙事方式。没有太多直白的台词，但通过场景、物品和细节把故事讲得很有味道。", new("", "鹿川", "design:comment-2"), "2024-03-10 12:18", 64, false,
            [new("r2-1", "r2", "是的，第一次玩可能只关注战斗，后面再看会发现很多伏笔和细节。", new("", "言一", "design:reply-2"), "2024-03-10 12:56", 19, false, [], [])], [])
    ];

    private static Story Sample(int number, string title, string author, int comments) =>
        new($"design-{number}", Source.DEMO, title, title, Author: author, Image: $"design:{number}", CommentCount: comments);
}
