"""Validated, persisted lab parameters; all values have explicit units."""
import math
SCHEMA = [
 ('confidence','Tracking','Landmark confidence',.55,.1,.99,.01,'Minimum visibility and presence for required joints.'),
 ('margin','Tracking','Boundary margin',.025,0,.12,.005,'Fraction of image width excluded on each side of section dividers.'),
 ('calibration','Tracking','Calibration duration (s)',3,1,15,.5,'All three regions must stay visible for this long.'),
 ('speed_low','Speed','Low hand height',-.8,-3,3,.05,'(Shoulder Y − average wrist Y) / shoulder width. Maps to zero throttle.'),
 ('speed_high','Speed','High hand height',1.,-3,3,.05,'Same normalized measurement. Maps to full throttle.'),
 ('smooth','Speed','Smoothing time (s)',.3,.02,3,.01,'Higher = smoother but slower response.'),
 ('min_speed','Speed','Minimum travel speed (units/s)',2,0,30,.5,'Preview travel speed at zero throttle while tracked.'),
 ('max_speed','Speed','Maximum travel speed (units/s)',12,0,60,.5,'Preview travel speed at full throttle. Units are test-world units.'),
 ('hand_trigger','Direction','Hand height to turn',.15,-.5,2,.05,'Raised wrist above its shoulder, divided by shoulder width; mirrored screen-left hand turns left.'),
 ('hand_reset','Direction','Hand height to rearm',-.15,-2,1,.05,'Both wrists must return below this normalized height before another turn.'),
 ('lane_width','Direction','Lane spacing (units)',2,.5,5,.1,'Distance between adjacent test-world lanes.'),
 ('lane_time','Direction','Lane transition time (s)',.25,.05,2,.05,'How quickly the preview moves into the next lane.'),
 ('jump_hand_trigger','Jump','Both-hand height to jump',.8,.2,2,.05,'Each wrist must be this far above its own shoulder, divided by shoulder width.'),
 ('jump_hand_reset','Jump','Both-hand height to rearm jump',-.15,-2,.5,.05,'Both wrists must lower below this shoulder-relative height after the animation lands before another jump.'),
 ('clap_close','Jump','Hand distance to grab',.35,.1,1.2,.05,'Wrist distance divided by shoulder width. A grab triggers when hands move from apart to this distance or closer.'),
 ('clap_open','Jump','Hand distance to rearm grab',.9,.3,2,.05,'Separate wrists to at least this normalized distance before another grab.'),
 ('clap_cooldown','Jump','Minimum time between grabs (s)',.35,.1,2,.05,'Reject another grab until this time has elapsed and hands have separated.'),
 ('jump_duration','Jump','Air time (s)',.9,.2,3,.05,'Duration of one fixed parabolic jump.'),
 ('jump_height','Jump','Peak jump height (units)',1.5,.1,5,.1,'Peak height of the preview jump in test-world units.')
]
DEFAULTS = {row[0]:row[3] for row in SCHEMA}

def validate(data):
    if not isinstance(data,dict) or set(data)!=set(DEFAULTS):
        raise ValueError('Settings must contain exactly the supported parameter names.')
    result={}
    for key,group,label,default,low,high,step,help_text in SCHEMA:
        value=data[key]
        if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or not low<=value<=high:
            raise ValueError(f'{label} must be a finite number between {low} and {high}.')
        result[key]=float(value)
    for low,high in [('speed_low','speed_high'),('min_speed','max_speed'),('hand_reset','hand_trigger'),('jump_hand_reset','jump_hand_trigger'),('clap_close','clap_open')]:
        if result[low]>=result[high]: raise ValueError(f'{low} must be smaller than {high}.')
    return result
