"""Builds red-channel-mask.onnx, a tiny stand-in segmentation model for tests.

It has the shape of a background-removal model (one RGB image in, one mask out) and no learned
weights: the mask it returns is the image's red channel. Tests can then predict the exact mask.

    python3 make-red-channel-mask-model.py
"""
import onnx
from onnx import TensorProto, helper

SIZE = 32
image = helper.make_tensor_value_info("input", TensorProto.FLOAT, [1, 3, SIZE, SIZE])
mask = helper.make_tensor_value_info("output", TensorProto.FLOAT, [1, 1, SIZE, SIZE])
starts = helper.make_tensor("starts", TensorProto.INT64, [1], [0])
ends = helper.make_tensor("ends", TensorProto.INT64, [1], [1])
axes = helper.make_tensor("axes", TensorProto.INT64, [1], [1])
node = helper.make_node("Slice", ["input", "starts", "ends", "axes"], ["output"])
graph = helper.make_graph([node], "red-channel-mask", [image], [mask], initializer=[starts, ends, axes])
model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)], producer_name="prism-tests")
model.ir_version = 8
onnx.checker.check_model(model)
onnx.save(model, "red-channel-mask.onnx")
