const express = require('express');
const AWS = require('aws-sdk');

AWS.config.update({ region: 'us-west-1' });
const autoscaling = new AWS.AutoScaling();
const sqs = new AWS.SQS();

const router = express.Router();

router.get('/env', (req, res) => {
  const vars = {
    STACK_NAME: process.env.STACK_NAME,
    ALIGN_JOB_QUEUE: process.env.ALIGN_JOB_QUEUE,
    ALIGN_OVERLAPS_TABLE: process.env.ALIGN_OVERLAPS_TABLE,
    ALIGN_FRAME_TRANSFORMS_TABLE: process.env.ALIGN_FRAME_TRANSFORMS_TABLE,
    ALIGN_PROJECTS_TABLE: process.env.ALIGN_PROJECTS_TABLE,
    ALIGN_OBSERVATIONS_TABLE: process.env.ALIGN_OBSERVATIONS_TABLE,
    ALIGN_FRAMES_TABLE: process.env.ALIGN_FRAMES_TABLE,
    ALIGN_QUEUE_MONITOR_LAMBDA: process.env.ALIGN_QUEUE_MONITOR_LAMBDA,
    ALIGN_SCAN_S3_LAMBDA: process.env.ALIGN_SCAN_S3_LAMBDA,
    ALIGN_IMAGE_INTAKE_LAMBDA: process.env.ALIGN_IMAGE_INTAKE_LAMBDA,
    ALIGN_WORKERS: process.env.ALIGN_WORKERS,
    TILING_JOB_QUEUE: process.env.TILING_JOB_QUEUE,
    TILING_CHILD_TILES_TABLE: process.env.TILING_CHILD_TILES_TABLE,
    TILING_PARENT_TILES_TABLE: process.env.TILING_PARENT_TILES_TABLE,
    TILING_TILE_INTAKE_LAMBDA: process.env.TILING_TILE_INTAKE_LAMBDA,
    TILING_WORKERS: process.env.TILING_WORKERS,
    TILING_QUEUE_MONITOR_LAMBDA: process.env.TILING_QUEUE_MONITOR_LAMBDA,
    TILING_PARENT_JOB_CREATION_LAMBDA: process.env.TILING_PARENT_JOB_CREATION_LAMBDA,
  };
  console.log(vars);
  res.status(200).send(vars);
});

function queueLength(queueUrl, res) {
  const params = { QueueUrl: queueUrl, AttributeNames: ['ApproximateNumberOfMessages'] };
  sqs.getQueueAttributes(params, (err, data) => {
    if (err) {
      console.log(err, err.stack); // an error occurred
      res.err(err);
    } else {
      res.status(200).send({ QueueSize: data.Attributes.ApproximateNumberOfMessages });
    }
  });
}

router.get('/align', (req, res) => { queueLength(process.env.ALIGN_JOB_QUEUE, res); });
router.get('/tiling', (req, res) => { queueLength(process.env.TILING_JOB_QUEUE, res); });

function workerCount(workers, res) {
  const params = { AutoScalingGroupNames: [workers] };
  autoscaling.describeAutoScalingGroups(params, (err, data) => {
    if (err) {
      console.log(err, err.stack);
      res.err(err);
    } else if (data.AutoScalingGroups.length !== 1) {
      res.status(500).send('did not find autoscaling group');
    } else {
      res.status(200).send({
        NumWorkers: data.AutoScalingGroups[0].Instances.length,
        DesiredWorkers: data.AutoScalingGroups[0].DesiredCapacity,
      });
    }
  });
}

router.get('/align/workers', (req, res) => { workerCount(process.env.ALIGN_WORKERS, res); });
router.get('/tiling/workers', (req, res) => { workerCount(process.env.ALIGN_WORKERS, res); });

module.exports = router;
