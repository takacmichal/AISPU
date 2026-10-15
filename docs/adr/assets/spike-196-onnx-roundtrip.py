import numpy as np, lightgbm as lgb, onnxruntime as ort, time
from onnxmltools import convert_lightgbm
from onnxconverter_common import FloatTensorType
rng=np.random.default_rng(0); X=rng.random((20000,8),dtype=np.float32); y=X[:,0]*3+np.sin(X[:,1]*6)+rng.normal(0,.1,20000)
m=lgb.LGBMRegressor(n_estimators=200,verbose=-1).fit(X,y)
o=convert_lightgbm(m,initial_types=[('input',FloatTensorType([None,8]))],target_opset=15)
open('m.onnx','wb').write(o.SerializeToString())
s=ort.InferenceSession('m.onnx',providers=['CPUExecutionProvider'])
p1=m.predict(X[:1000]); p2=s.run(None,{'input':X[:1000]})[0].ravel()
print('max abs diff',float(np.abs(p1-p2).max()))
t=time.perf_counter(); 
for _ in range(200): s.run(None,{'input':X[:24]})
print('onnx 24 rows ms',(time.perf_counter()-t)/200*1000)
t=time.perf_counter()
for _ in range(200): m.predict(X[:24])
print('lgb 24 rows ms',(time.perf_counter()-t)/200*1000)
import os; print('onnx size kB',os.path.getsize('m.onnx')//1024)
