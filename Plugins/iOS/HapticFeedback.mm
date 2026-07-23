#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator* SoftGenerator;
static UIImpactFeedbackGenerator* MediumGenerator;
static UIImpactFeedbackGenerator* HeavyGenerator;

static void EnsureGenerators(void)
{
    if (SoftGenerator == nil)
    {
        SoftGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        MediumGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        HeavyGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        [SoftGenerator prepare];
        [MediumGenerator prepare];
        [HeavyGenerator prepare];
    }
}

void _HapticLight(void)
{
    EnsureGenerators();
    [SoftGenerator impactOccurred];
    [SoftGenerator prepare];
}

void _HapticMedium(void)
{
    EnsureGenerators();
    [MediumGenerator impactOccurred];
    [MediumGenerator prepare];
}

void _HapticHeavy(void)
{
    EnsureGenerators();
    [HeavyGenerator impactOccurred];
    [HeavyGenerator prepare];
}
