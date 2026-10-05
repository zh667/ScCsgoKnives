"""Player-facing package text. Revision and serialization details belong in release metadata."""
BASE_DESCRIPTION = 'CS风格枪械、刀具与投掷物，包含武器皮肤、切枪换弹与检视动画、武器装配台制作维修、击杀计数与枪械成长，以及自定义触屏操作。'

def presentation(version, edition):
    name = 'CS武器 · ' + ('轻量版' if edition == 'lite' else '全量版')
    description = BASE_DESCRIPTION
    if version in ('1.3.0', '1.4.0'):
        description += ('轻量资源版本，可搭配探员包使用。' if edition == 'lite'
                        else '包含探员、战术同伴、敌队挑战、人物外观与中英语音。')
    return name, description
