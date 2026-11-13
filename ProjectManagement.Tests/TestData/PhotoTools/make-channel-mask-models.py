"""Builds red-channel-mask.onnx and green-channel-mask.onnx, tiny stand-in segmentation models
for tests.

They have the shape of a background-removal model (one RGB image in, one mask out) and no learned
weights: the mask is the image's red (or green) channel. Tests can then predict the exact mask,
and tell the two models apart when one replaces the other.

    python3 make-red-channel-mask-model.py
"""
import onnx
from onnx import TensorProto, helper

SIZE = 32
image = helper.make_tensor_value_info("input", TensorProto.FLOAT, [1, 3, SIZE, SIZE])
mask = helper.make_tensor_value_info("output", TensorProto.FLOAT, [1, 1, SIZE, SIZE])
def build(channel, name):
    starts = helper.make_tensor("starts", TensorProto.INT64, [1], [channel])
    ends = helper.make_tensor("ends", TensorProto.INT64, [1], [channel + 1])
    axes = helper.make_tensor("axes", TensorProto.INT64, [1], [1])
    node = helper.make_node("Slice", ["input", "starts", "ends", "axes"], ["output"])
    graph = helper.make_graph([node], name, [image], [mask], initializer=[starts, ends, axes])
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)], producer_name="prism-tests")
    model.ir_version = 8
    onnx.checker.check_model(model)
    onnx.save(model, name + ".onnx")


build(0, "red-channel-mask")
build(1, "green-channel-mask")
